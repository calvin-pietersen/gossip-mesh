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
        private readonly object _memberLocker = new Object();
        private readonly List<Member> _members = new List<Member>();

        private UdpClient _udpServer;

        private readonly ILogger _logger;

        public Server(int listenPort, int protocolPeriodMs, ILogger logger, List<IPEndPoint> seedMembers = null)
        {
            _protocolPeriodMs = protocolPeriodMs;
            _seedMembers = seedMembers ?? new List<IPEndPoint>();

            _self = new Member
            {
                IP = IPAddress.Any,
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
            _udpServer = CreateUdpClient(_self.GossipEndpoint);

            _logger.LogInformation("Starting Gossip.Mesh server");
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
                    // ping member
                    if (_members.Count > 0)
                    {
                        var i = rand.Next(0, _members.Count);
                        await PingAsync(_udpServer, _members[i].GossipEndpoint);
                    }

                    // ping seed
                    if (_seedMembers.Count > 0)
                    {
                        var i = rand.Next(0, _seedMembers.Count);
                        await PingAsync(_udpServer, _seedMembers[i]);
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
                    _logger.LogInformation("Gossip.Mesh recieved {MessageType} from {Member}", messageType, request.RemoteEndPoint);

                    // update members
                    UpdateMembers(request.Buffer);

                    if (messageType == MessageType.Ping)
                    {
                        // ack
                        await AckAsync(_udpServer, request.RemoteEndPoint).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception");
            }
        }

        public async Task PingAsync(UdpClient udpClient, IPEndPoint endpoint)
        {
            _logger.LogInformation("Gossip.Mesh sending ping to {endpoint}", endpoint);

            using (var stream = new MemoryStream(508))
            {
                stream.WriteByte((byte)MessageType.Ping);
                WriteMembers(stream);

                await udpClient.SendAsync(stream.GetBuffer(), 508, endpoint).ConfigureAwait(false);
            }
        }

        public async Task AckAsync(UdpClient udpClient, IPEndPoint endpoint)
        {
            _logger.LogInformation("Gossip.Mesh sending ack to {endpoint}", endpoint);

            using (var stream = new MemoryStream(508))
            {
                stream.WriteByte((byte)MessageType.Ack);
                WriteMembers(stream);

                await udpClient.SendAsync(stream.GetBuffer(), 508, endpoint).ConfigureAwait(false);
            }
        }

        private void UpdateMembers(byte[] bytes)
        {
            
        }

        private void WriteMembers(Stream stream)
        {
            var i = 0;
            lock (_memberLocker)
            {
                while (i < _members.Count && stream.Position < 507 - 10)
                {
                    _members[i].WriteTo(stream);
                    i++;
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception");
            }
             return udpClient;
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