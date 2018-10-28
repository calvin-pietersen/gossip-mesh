﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly int _ackTimeoutMs;
        private readonly List<IPEndPoint> _seedMembers;
        private volatile bool _bootstrapping;
        private readonly object _memberLocker = new Object();
        private readonly Dictionary<IPEndPoint, Member> _members = new Dictionary<IPEndPoint, Member>();
        private readonly object _awaitingAckMembersLock = new Object();
        private volatile Dictionary<IPEndPoint, DateTime> _awaitingAckMembers = new Dictionary<IPEndPoint, DateTime>();
        private DateTime _lastProtocolPeriod = DateTime.Now;
        private UdpClient _udpServer;

        private readonly ILogger _logger;

        public Server(int listenPort, int protocolPeriodMs, int ackTimeoutMs, ILogger logger, List<IPEndPoint> seedMembers = null)
        {
            _protocolPeriodMs = protocolPeriodMs;
            _ackTimeoutMs = ackTimeoutMs;
            _seedMembers = seedMembers;
            _bootstrapping = seedMembers != null && seedMembers.Count > 0;

            _self = new Member
            {
                State = MemberState.Alive,
                IP = GetLocalIPAddress(),
                GossipPort = (ushort)listenPort,
                Generation = 1,
                Service = 1,
                ServicePort = 8080
            };

            _logger = logger;
        }

        public void Start()
        {
            _logger.LogInformation("Starting Gossip.Mesh server");

            _udpServer = CreateUdpClient(_self.GossipEndPoint);

            // bootstrap
            Task.Run(async () => await Bootstrap().ConfigureAwait(false)).ConfigureAwait(false);

            // recieve requests
            Task.Run(async () => await Listener().ConfigureAwait(false)).ConfigureAwait(false);

            // gossip
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
                        var i = rand.Next(0, members.Length);
                        var member = members[i];

                        await PingAsync(_udpServer, member.GossipEndPoint, members);

                        AddAwaitingAck(member.GossipEndPoint);
                        await Task.Delay(_ackTimeoutMs).ConfigureAwait(false);

                        // check was not acked
                        if (CheckWasNotAcked(member.GossipEndPoint))
                        {
                            // TODO use random traversal
                            var indirectEndpoints = Enumerable
                                .Range(0, Math.Min(2, members.Length))
                                .OrderBy(x => rand.Next())
                                    .Select(k => members[k].GossipEndPoint)
                                .ToArray();

                            await PingRequestAsync(_udpServer, member.GossipEndPoint, indirectEndpoints, members);
                            await Task.Delay(_ackTimeoutMs).ConfigureAwait(false);

                            if (CheckWasNotAcked(member.GossipEndPoint))
                            {
                                lock (_memberLocker)
                                {
                                    _members[member.GossipEndPoint].State = MemberState.Suspected;
                                    // add to dead check
                                }
                            }
                        }
                    }

                    var syncTime = Math.Min(_protocolPeriodMs, (DateTime.Now - _lastProtocolPeriod).Milliseconds);
                    await Task.Delay(syncTime).ConfigureAwait(false);
                    _lastProtocolPeriod = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception");
            }
        }

        public async Task Listener()
        {
            try
            {
                while (true)
                {
                    // recieve
                    var request = await _udpServer.ReceiveAsync().ConfigureAwait(false);
                    using (var stream = new MemoryStream(request.Buffer, false))
                    {

                        var messageType = (MessageType)stream.ReadByte();

                        // finish bootrapping
                        if (_bootstrapping)
                        {
                            _logger.LogInformation("Gossip.Mesh finished bootstrapping");
                            _bootstrapping = false;
                        }

                        _logger.LogInformation("Gossip.Mesh recieved {MessageType} from {RemoteEndPoint}", messageType, request.RemoteEndPoint);

                        IPEndPoint destinationEndPoint;
                        IPEndPoint sourceEndPoint;
                        
                        if (messageType == MessageType.Ping || messageType == MessageType.Ack)
                        {
                            sourceEndPoint = request.RemoteEndPoint;
                            destinationEndPoint = _self.GossipEndPoint;
                        }
                        else
                        {
                            destinationEndPoint = new IPEndPoint(stream.ReadIPAddress(), stream.ReadPort());
                            sourceEndPoint = new IPEndPoint(stream.ReadIPAddress(), stream.ReadPort());
                        }

                        // update members
                        UpdateMembers(stream);

                        var members = GetMembers();

                        if (messageType == MessageType.Ping)
                        {
                            // ack
                            await AckAsync(_udpServer, sourceEndPoint, members).ConfigureAwait(false);
                        }

                        else if (messageType == MessageType.Ack)
                        {
                            RemoveAwaitingAck(sourceEndPoint);
                        }

                        else if (messageType == MessageType.PingRequest)
                        {
                            // if we are the destination send an ack request
                            if (destinationEndPoint == _self.GossipEndPoint)
                            {
                                await AckRequestAsync(_udpServer, destinationEndPoint, request.RemoteEndPoint, members).ConfigureAwait(false);
                            }

                            // otherwise forward the request
                            else
                            {
                                await PingRequestForwardAsync(_udpServer, destinationEndPoint, sourceEndPoint, request.Buffer).ConfigureAwait(false);
                            }
                        }

                        else if (messageType == MessageType.AckRequest)
                        {
                            // if we are the destination clear awaiting ack
                            if (destinationEndPoint == _self.GossipEndPoint)
                            {
                                RemoveAwaitingAck(sourceEndPoint);
                            }
                            // otherwirse forward the request
                            else
                            {
                                await AckRequestForwardAsync(_udpServer, destinationEndPoint, sourceEndPoint, request.Buffer).ConfigureAwait(false);
                            }
                        }
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
            if (_bootstrapping)
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
            else
            {
                _logger.LogInformation("Gossip.Mesh no seeds to bootstrap off");
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

        public async Task PingRequestAsync(UdpClient udpClient, IPEndPoint destinationEndpoint, IPEndPoint[] indirectEndpoints, Member[] members = null)
        {
            foreach (var indirectEndpoint in indirectEndpoints)
            {
                _logger.LogInformation("Gossip.Mesh sending ping request to {destinationEndpoint} via {indirectEndpoint}", destinationEndpoint, indirectEndpoint);

                using (var stream = new MemoryStream(508))
                {
                    stream.WriteByte((byte)MessageType.PingRequest);

                    stream.Write(destinationEndpoint.Address.GetAddressBytes(), 0, 4);
                    stream.WriteByte((byte)destinationEndpoint.Port);
                    stream.WriteByte((byte)(destinationEndpoint.Port >> 8));

                    stream.Write(_self.IP.GetAddressBytes(), 0, 4);
                    stream.WriteByte((byte)_self.GossipPort);
                    stream.WriteByte((byte)(_self.GossipPort >> 8));

                    WriteMembers(stream, members);

                    await udpClient.SendAsync(stream.GetBuffer(), 508, indirectEndpoint).ConfigureAwait(false);
                }
            }
        }

        public async Task PingRequestForwardAsync(UdpClient udpClient, IPEndPoint destinationEndPoint, IPEndPoint sourceEndPoint, byte[] request)
        {
            _logger.LogInformation("Gossip.Mesh forwarding ping request to {destinationEndPoint} from {sourceEndPoint}", destinationEndPoint, sourceEndPoint);

            await udpClient.SendAsync(request, 508, destinationEndPoint).ConfigureAwait(false);
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

        public async Task AckRequestAsync(UdpClient udpClient, IPEndPoint destinationEndpoint, IPEndPoint indirectEndPoint, Member[] members)
        {
            _logger.LogInformation("Gossip.Mesh sending ack request to {destinationEndpoint} via {indirectEndPoint}", destinationEndpoint, indirectEndPoint);

            using (var stream = new MemoryStream(508))
            {
                stream.WriteByte((byte)MessageType.AckRequest);

                stream.Write(destinationEndpoint.Address.GetAddressBytes(), 0, 4);
                stream.WriteByte((byte)destinationEndpoint.Port);
                stream.WriteByte((byte)(destinationEndpoint.Port >> 8));

                stream.Write(_self.IP.GetAddressBytes(), 0, 4);
                stream.WriteByte((byte)_self.GossipPort);
                stream.WriteByte((byte)(_self.GossipPort >> 8));

                WriteMembers(stream, members);

                await udpClient.SendAsync(stream.GetBuffer(), 508, indirectEndPoint).ConfigureAwait(false);
            }
        }

        public async Task AckRequestForwardAsync(UdpClient udpClient, IPEndPoint destinationEndPoint, IPEndPoint sourceEndPoint, byte[] request)
        {
            _logger.LogInformation("Gossip.Mesh forwarding ack request to {destinationEndPoint} from {sourceEndPoint}", destinationEndPoint, sourceEndPoint);

            await udpClient.SendAsync(request, 508, destinationEndPoint).ConfigureAwait(false);
        }

        private void UpdateMembers(Stream stream)
        {
            while (stream.Position < stream.Length)
            {
                var memberState = (MemberState)stream.ReadByte();
                var ip = stream.ReadIPAddress();
                var gossipPort = stream.ReadPort();
                var generation = (byte)stream.ReadByte();

                var ipEndPoint = new IPEndPoint(ip, gossipPort);

                if (memberState == MemberState.Alive)
                {
                    var servicePort = stream.ReadPort();
                    var service = (byte)stream.ReadByte();

                    // we don't add ourselves to the member list
                    if (ipEndPoint.Port != _self.GossipEndPoint.Port || !ipEndPoint.Address.Equals(_self.GossipEndPoint.Address))
                    {
                        lock (_memberLocker)
                        {
                            if (!_members.ContainsKey(ipEndPoint))
                            {
                                _members.Add(ipEndPoint, new Member
                                {
                                    State = memberState,
                                    IP = ip,
                                    GossipPort = gossipPort,
                                    Generation = generation,
                                    ServicePort = servicePort,
                                    Service = service,
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
                else
                {
                    if (ipEndPoint.Port != _self.GossipEndPoint.Port || !ipEndPoint.Address.Equals(_self.GossipEndPoint.Address))
                    {
                        lock (_memberLocker)
                        {
                            if (_members.ContainsKey(ipEndPoint) && _members[ipEndPoint].Generation > generation)
                            {
                                _members[ipEndPoint].State = memberState;
                                _members[ipEndPoint].Generation = generation;
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

            // TODO - don't just iterate over the members, do the least gossiped members.... dah
            if (members != null)
            {
                var i = 0;
                {
                    while (i < members.Length && stream.Position < 508 - 20)
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
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
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

        private static IPAddress GetLocalIPAddress()
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

        private void AddAwaitingAck(IPEndPoint endPoint)
        {
            lock (_awaitingAckMembersLock)
            {
                if (!_awaitingAckMembers.ContainsKey(endPoint))
                {
                    _awaitingAckMembers.Add(endPoint, DateTime.Now);
                }
            }
        }

        private bool CheckWasNotAcked(IPEndPoint ipEndPoint)
        {
            var wasNotAcked = false;
            lock (_awaitingAckMembersLock)
            {
                wasNotAcked = _awaitingAckMembers.ContainsKey(ipEndPoint);
            }

            return wasNotAcked;
        }

        private void RemoveAwaitingAck(IPEndPoint endPoint)
        {
            lock (_awaitingAckMembersLock)
            {
                if (_awaitingAckMembers.ContainsKey(endPoint))
                {
                    _awaitingAckMembers.Remove(endPoint);
                }
            }
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