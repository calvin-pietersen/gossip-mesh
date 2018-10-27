using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GossipMesh.Core
{
    public class Server : IDisposable
    {
        private readonly Member _self;
        private readonly int _protocolPeriodMs;
        private readonly List<IPEndPoint> _seedMembers;
        private bool _bootstrapping = true;
        private readonly object _memberLocker = new Object();
        private readonly Dictionary<IPEndPoint, Member> _members = new Dictionary<IPEndPoint, Member>();

        private UdpClient _udpServer;

        private readonly ILogger _logger;

        public Server(int listenPort, int protocolPeriodMs, ILogger logger, List<IPEndPoint> seedMembers = null)
        {
            _protocolPeriodMs = protocolPeriodMs;
            _seedMembers = seedMembers ?? new List<IPEndPoint>();

            _self = new Member
            {
                IP = GetLocalIPAddress(),
                GossipPort = (ushort)listenPort,
                ServicePort = 8080,
                ServiceId = 1,
                Generation = 1,
                State = MemberState.Alive
            };

            _logger = logger;
        }

        public void Start()
        {
            _logger.LogInformation("Starting Gossip.Mesh server");

            _udpServer = CreateUdpClient(_self.GossipEndPoint);

            // TODO - bootstrap here
            Task.Run(async () => await Bootstrap().ConfigureAwait(false)).ConfigureAwait(false);

            // recieve
            Task.Run(async () => await StartListener().ConfigureAwait(false)).ConfigureAwait(false);

            // ping
            Task.Run(async () => await GossipPump().ConfigureAwait(false)).ConfigureAwait(false);
        }

        public async Task GossipPump()
        {
            try
            {
                Random rand = new Random();

                while (true)
                {
                    var members = GetMembers();

                    // ping member
                    if (members.Length > 0)
                    {
                        if (members != null)
                        {
                            var i = rand.Next(0, members.Length);
                            await PingAsync(_udpServer, members[i].GossipEndPoint, members);
                        }
                    }

                    // TODO - sync times between protocol period better should be protocol time - last pump execution time
                    await Task.Delay(_protocolPeriodMs).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception");
            }
        }

        public async Task StartListener()
        {
            try
            {
                while (true)
                {
                    // recieve
                    var request = await _udpServer.ReceiveAsync().ConfigureAwait(false);
                    var messageType = (MessageType)request.Buffer[0];

                    // finish bootrapping
                    if(_bootstrapping) 
                    {
                        _logger.LogInformation("Gossip.Mesh finished bootstrapping");
                        _bootstrapping = false;
                    }

                    _logger.LogInformation("Gossip.Mesh recieved {MessageType} from {Member}", messageType, request.RemoteEndPoint);

                    using (var stream = new MemoryStream(request.Buffer, false))
                    {
                        // update members
                        UpdateMembers(stream);
                    }

                    if (messageType == MessageType.Ping)
                    {
                        var members = GetMembers();
                        // ack
                        await AckAsync(_udpServer, request.RemoteEndPoint, members).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception");
            }
        }

        public async Task Bootstrap()
        {
            _logger.LogInformation("Gossip.Mesh bootstrapping off seeds");
            Random rand = new Random();

            // ideally we want to bootstap over tcp but for now we will ping seeds and stop bootstrapping on the first ack
            while (_bootstrapping)
            {
                // ping seed
                var i = rand.Next(0, _seedMembers.Count);
                await PingAsync(_udpServer, _seedMembers[i]);

                await Task.Delay(_protocolPeriodMs).ConfigureAwait(false);
            }
        }

        public async Task PingAsync(UdpClient udpClient, IPEndPoint endpoint, Member[] members = null)
        {
            _logger.LogInformation("Gossip.Mesh sending ping to {endpoint}", endpoint);

            using (var stream = new MemoryStream(508))
            {
                stream.WriteByte((byte)MessageType.Ping);
                WriteMembers(stream, members);

                await udpClient.SendAsync(stream.GetBuffer(), 508, endpoint).ConfigureAwait(false);
            }
        }

        public async Task AckAsync(UdpClient udpClient, IPEndPoint endpoint, Member[] members)
        {
            _logger.LogInformation("Gossip.Mesh sending ack to {endpoint}", endpoint);

            using (var stream = new MemoryStream(508))
            {
                stream.WriteByte((byte)MessageType.Ack);
                WriteMembers(stream, members);

                await udpClient.SendAsync(stream.GetBuffer(), 508, endpoint).ConfigureAwait(false);
            }
        }

        private void UpdateMembers(Stream stream)
        {
            stream.Seek(1, SeekOrigin.Begin);

            while (stream.Position < stream.Length)
            {
                var memberState = (MemberState)stream.ReadByte();
                if (memberState == MemberState.Alive)
                {

                    var ip = new IPAddress(new byte[] { (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte() });
                    var gossipPort = BitConverter.ToUInt16(new byte[] { (byte)stream.ReadByte(), (byte)stream.ReadByte() }, 0);
                    var servicePort = BitConverter.ToUInt16(new byte[] { (byte)stream.ReadByte(), (byte)stream.ReadByte() }, 0);
                    var serviceId = (byte)stream.ReadByte();
                    var generation = (byte)stream.ReadByte();

                    var ipEndPoint = new IPEndPoint(ip, gossipPort);

                    // we don't add ourselves to the member list
                    if (ipEndPoint.Port != _self.GossipEndPoint.Port || !ipEndPoint.Address.Equals(_self.GossipEndPoint.Address))
                    {
                        lock (_memberLocker)
                        {
                            if (!_members.ContainsKey(ipEndPoint))
                            {
                                _members.Add(ipEndPoint, new Member
                                {
                                    IP = ip,
                                    GossipPort = gossipPort,
                                    ServicePort = servicePort,
                                    ServiceId = serviceId,
                                    Generation = generation,
                                    State = memberState
                                });
                            }
                            else
                            {
                                if (_members[ipEndPoint].Generation > generation)
                                {
                                    _members[ipEndPoint].Generation = generation;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void WriteMembers(Stream stream, Member[] members)
        {
            // always prioritize ourselves
            _self.WriteTo(stream);

            // don't just iterate over the members, do the least gossiped members.... dah
            if (members != null)
            {
                var i = 0;
                {
                    while (i < members.Length && stream.Position < 507 - 20)
                    {
                        var member = members[i];
                        members[i].WriteTo(stream);
                        i++;
                    }
                }
            }
        }

        private UdpClient CreateUdpClient(EndPoint endPoint)
        {
            var udpClient = new UdpClient();
            try
            {
                udpClient.Client.SetSocketOption(
                            SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(endPoint);
                udpClient.DontFragment = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception");
            }
            return udpClient;
        }

        private Member[] GetMembers()
        {
            Member[] members = null;
            lock (_memberLocker)
            {
                members = new Member[_members.Count];
                _members.Values.CopyTo(members, 0);
            }

            return members;
        }

        public static IPAddress GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip;
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        public void Dispose()
        {
            if (_udpServer != null)
            {
                _udpServer.Close();
            }
        }
    }
}