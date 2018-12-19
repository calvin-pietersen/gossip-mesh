using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GossipMesh.Core
{
    public class Gossiper : IDisposable
    {
        private readonly Member _self;
        private readonly int _maxUdpPacketBytes;
        private readonly int _protocolPeriodMs;
        private readonly int _ackTimeoutMs;
        private readonly int _numberOfIndirectEndpoints;
        private readonly IPEndPoint[] _seedMembers;
        private volatile bool _bootstrapping;
        private readonly object _memberLocker = new Object();
        private readonly Dictionary<IPEndPoint, Member> _members = new Dictionary<IPEndPoint, Member>();
        private readonly object _awaitingAcksLock = new Object();
        private volatile Dictionary<IPEndPoint, DateTime> _awaitingAcks = new Dictionary<IPEndPoint, DateTime>();
        private DateTime _lastProtocolPeriod = DateTime.Now;
        private readonly Random _rand = new Random();
        private UdpClient _udpServer;
        private IStateListener _listener;

        private readonly ILogger _logger;

        public Gossiper(GossiperOptions options, ILogger logger)
        {
            _maxUdpPacketBytes = options.MaxUdpPacketBytes;
            _protocolPeriodMs = options.ProtocolPeriodMilliseconds;
            _ackTimeoutMs = options.AckTimeoutMilliseconds;
            _numberOfIndirectEndpoints = options.NumberOfIndirectEndpoints;
            _seedMembers = options.SeedMembers;
            _bootstrapping = options.SeedMembers != null && options.SeedMembers.Length > 0;
            _listener = options.StateListener;

            _self = new Member
            {
                State = MemberState.Alive,
                IP = options.ListenAddress,
                GossipPort = options.ListenPort,
                Generation = 1,
                Service = options.Service,
                ServicePort = options.ServicePort
            };

            _logger = logger;
        }

        public void Start()
        {
            _logger.LogInformation("Starting Gossip.Mesh Gossiper on {GossipEndPoint}", _self.GossipEndPoint);

            _udpServer = CreateUdpClient(_self.GossipEndPoint);

            // bootstrap
            Task.Run(async () => await Bootstraper().ConfigureAwait(false)).ConfigureAwait(false);

            // receive requests
            Task.Run(async () => await Listener().ConfigureAwait(false)).ConfigureAwait(false);

            // gossip
            Task.Run(async () => await Gossip().ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task Bootstraper()
        {
            if (_bootstrapping)
            {
                _logger.LogInformation("Gossip.Mesh bootstrapping off seeds");

                // ideally we want to bootstap over tcp but for now we will ping seeds and stop bootstrapping on the first ack
                while (_bootstrapping)
                {
                    try
                    {
                        // ping seed
                        var i = _rand.Next(0, _seedMembers.Length);
                        await PingAsync(_udpServer, _seedMembers[i]);
                    }

                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
                    }

                    await Task.Delay(_protocolPeriodMs).ConfigureAwait(false);
                }
            }

            else
            {
                _logger.LogInformation("Gossip.Mesh no seeds to bootstrap off");
            }
        }

        private async Task Listener()
        {
            while (true)
            {
                try
                {
                    var request = await _udpServer.ReceiveAsync().ConfigureAwait(false);
                    using (var stream = new MemoryStream(request.Buffer, false))
                    {
                        var messageType = (MessageType)stream.ReadByte();
                        _logger.LogDebug("Gossip.Mesh recieved {MessageType} from {RemoteEndPoint}", messageType, request.RemoteEndPoint);

                        // finish bootrapping
                        if (_bootstrapping)
                        {
                            _bootstrapping = false;
                            _logger.LogInformation("Gossip.Mesh finished bootstrapping");
                        }

                        IPEndPoint destinationGossipEndPoint;
                        IPEndPoint sourceGossipEndPoint;

                        if (messageType == MessageType.Ping || messageType == MessageType.Ack)
                        {
                            sourceGossipEndPoint = request.RemoteEndPoint;
                            destinationGossipEndPoint = _self.GossipEndPoint;
                        }

                        else
                        {
                            destinationGossipEndPoint = stream.ReadIPEndPoint();
                            sourceGossipEndPoint = stream.ReadIPEndPoint();
                        }

                        // update members
                        UpdateMembers(stream);

                        var members = GetMembers();
                        await RequestHandler(request, messageType, sourceGossipEndPoint, destinationGossipEndPoint, members).ConfigureAwait(false);
                    }
                }

                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
                }
            }
        }

        private async Task Gossip()
        {
            while (true)
            {
                try
                {
                    var members = GetMembers();

                    // ping member
                    if (members.Length > 0)
                    {
                        var i = _rand.Next(0, members.Length);
                        var member = members[i];

                        AddAwaitingAck(member.GossipEndPoint);
                        await PingAsync(_udpServer, member.GossipEndPoint, members);

                        await Task.Delay(_ackTimeoutMs).ConfigureAwait(false);

                        // check was not acked
                        if (CheckWasNotAcked(member.GossipEndPoint))
                        {
                            var indirectEndpoints = GetIndirectGossipEndPoints(member.GossipEndPoint, members);

                            await PingRequestAsync(_udpServer, member.GossipEndPoint, indirectEndpoints, members);

                            await Task.Delay(_ackTimeoutMs).ConfigureAwait(false);

                            if (CheckWasNotAcked(member.GossipEndPoint))
                            {
                                HandleSuspiciousMember(member.GossipEndPoint);
                            }
                        }
                    }

                    HandleDeadMembers();
                }

                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
                }

                await WaitForProtocolPeriod().ConfigureAwait(false);
            }
        }

        private async Task RequestHandler(UdpReceiveResult request, MessageType messageType, IPEndPoint sourceGossipEndPoint, IPEndPoint destinationGossipEndPoint, Member[] members)
        {
            if (messageType == MessageType.Ping)
            {
                await AckAsync(_udpServer, sourceGossipEndPoint, members).ConfigureAwait(false);
            }

            else if (messageType == MessageType.Ack)
            {
                RemoveAwaitingAck(sourceGossipEndPoint);
            }

            else if (messageType == MessageType.PingRequest)
            {
                // if we are the destination send an ack request
                if (EndPointsMatch(destinationGossipEndPoint, _self.GossipEndPoint))
                {
                    await AckRequestAsync(_udpServer, sourceGossipEndPoint, request.RemoteEndPoint, members).ConfigureAwait(false);
                }

                // otherwise forward the request
                else
                {
                    await PingRequestForwardAsync(_udpServer, destinationGossipEndPoint, sourceGossipEndPoint, request.Buffer).ConfigureAwait(false);
                }
            }

            else if (messageType == MessageType.AckRequest)
            {
                // if we are the destination clear awaiting ack
                if (EndPointsMatch(destinationGossipEndPoint, _self.GossipEndPoint))
                {
                    RemoveAwaitingAck(sourceGossipEndPoint);
                }

                // otherwirse forward the request
                else
                {
                    await AckRequestForwardAsync(_udpServer, destinationGossipEndPoint, sourceGossipEndPoint, request.Buffer).ConfigureAwait(false);
                }
            }
        }

        private async Task PingAsync(UdpClient udpClient, IPEndPoint destinationGossipEndPoint, Member[] members = null)
        {
            _logger.LogDebug("Gossip.Mesh sending Ping to {destinationGossipEndPoint}", destinationGossipEndPoint);

            using (var stream = new MemoryStream(_maxUdpPacketBytes))
            {
                stream.WriteByte((byte)MessageType.Ping);
                WriteMembers(stream, members);

                await udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, destinationGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task PingRequestAsync(UdpClient udpClient, IPEndPoint destinationGossipEndPoint, IEnumerable<IPEndPoint> indirectEndpoints, Member[] members = null)
        {
            foreach (var indirectEndpoint in indirectEndpoints)
            {
                _logger.LogDebug("Gossip.Mesh sending PingRequest to {destinationGossipEndPoint} via {indirectEndpoint}", destinationGossipEndPoint, indirectEndpoint);

                using (var stream = new MemoryStream(_maxUdpPacketBytes))
                {
                    stream.WriteByte((byte)MessageType.PingRequest);

                    stream.WriteIPEndPoint(destinationGossipEndPoint);
                    stream.WriteIPEndPoint(_self.GossipEndPoint);

                    WriteMembers(stream, members);

                    await udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, indirectEndpoint).ConfigureAwait(false);
                }
            }
        }

        private async Task PingRequestForwardAsync(UdpClient udpClient, IPEndPoint destinationGossipEndPoint, IPEndPoint sourceGossipEndPoint, byte[] request)
        {
            _logger.LogDebug("Gossip.Mesh forwarding PingRequest to {destinationGossipEndPoint} from {sourceGossipEndPoint}", destinationGossipEndPoint, sourceGossipEndPoint);

            await udpClient.SendAsync(request, request.Length, destinationGossipEndPoint).ConfigureAwait(false);
        }

        private async Task AckAsync(UdpClient udpClient, IPEndPoint destinationGossipEndPoint, Member[] members)
        {
            _logger.LogDebug("Gossip.Mesh sending Ack to {destinationGossipEndPoint}", destinationGossipEndPoint);

            using (var stream = new MemoryStream(_maxUdpPacketBytes))
            {
                stream.WriteByte((byte)MessageType.Ack);
                WriteMembers(stream, members);

                await udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, destinationGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task AckRequestAsync(UdpClient udpClient, IPEndPoint destinationGossipEndPoint, IPEndPoint indirectEndPoint, Member[] members)
        {
            _logger.LogDebug("Gossip.Mesh sending AckRequest to {destinationGossipEndPoint} via {indirectEndPoint}", destinationGossipEndPoint, indirectEndPoint);

            using (var stream = new MemoryStream(_maxUdpPacketBytes))
            {
                stream.WriteByte((byte)MessageType.AckRequest);

                stream.WriteIPEndPoint(destinationGossipEndPoint);
                stream.WriteIPEndPoint(_self.GossipEndPoint);

                WriteMembers(stream, members);

                await udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, indirectEndPoint).ConfigureAwait(false);
            }
        }

        private async Task AckRequestForwardAsync(UdpClient udpClient, IPEndPoint destinationGossipEndPoint, IPEndPoint sourceGossipEndPoint, byte[] request)
        {
            _logger.LogDebug("Gossip.Mesh forwarding AckRequest to {destinationGossipEndPoint} from {sourceGossipEndPoint}", destinationGossipEndPoint, sourceGossipEndPoint);

            await udpClient.SendAsync(request, request.Length, destinationGossipEndPoint).ConfigureAwait(false);
        }

        private void UpdateMembers(Stream stream)
        {
            while (stream.Position < stream.Length)
            {
                var newMember = Member.ReadFrom(stream);

                // we don't add ourselves to the member list
                if (!EndPointsMatch(newMember.GossipEndPoint, _self.GossipEndPoint))
                {
                    lock (_memberLocker)
                    {
                        if (_members.TryGetValue(newMember.GossipEndPoint, out var oldMember) &&
                            (oldMember.IsLaterGeneration(newMember.Generation) || 
                            (oldMember.Generation == newMember.Generation && oldMember.IsStateSuperseded(newMember.State))))
                        {
                            RemoveAwaitingAck(newMember.GossipEndPoint); // stops dead claim escalation
                            _members[newMember.GossipEndPoint] = newMember;
                            _logger.LogInformation("Gossip.Mesh member state changed {member}", newMember);
                            _listener.MemberStateUpdated(newMember);
                        }

                        else if (oldMember == null)
                        {
                            _members.Add(newMember.GossipEndPoint, newMember);
                            _logger.LogInformation("Gossip.Mesh member added {member}", newMember);
                            _listener.MemberStateUpdated(newMember);
                        }
                    }
                }

                // handle any state claims about ourselves
                else if (_self.IsLaterGeneration(newMember.Generation) || 
                        (newMember.State != MemberState.Alive && newMember.Generation == _self.Generation))
                {
                    _self.Generation = (byte)(newMember.Generation + 1);
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
                    while (i < members.Length && stream.Position < _maxUdpPacketBytes - 11)
                    {
                        var member = members[i];
                        members[i].WriteTo(stream);
                        i++;
                    }
                }
            }
        }

        private UdpClient CreateUdpClient(EndPoint listenEndPoint)
        {
            var udpClient = new UdpClient();
            try
            {
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(listenEndPoint);
                udpClient.DontFragment = true;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
            }

            return udpClient;
        }

        private Member[] GetMembers()
        {
            lock (_memberLocker)
            {
                return _members
                    .Values
                    .OrderBy(m => m.GossipCounter)
                    .ToArray();
            }
        }

        private IEnumerable<IPEndPoint> GetIndirectGossipEndPoints(IPEndPoint directGossipEndPoint, Member[] members)
        {
            if (members.Length <= 1)
            {
                return Enumerable.Empty<IPEndPoint>();
            }

            var randomIndex = _rand.Next(0, members.Length);

            return Enumerable.Range(randomIndex, _numberOfIndirectEndpoints + 1)
                .Select(ri => ri % members.Length) // wrap the range around to 0 if we hit the end
                .Select(i => members[i])
                .Where(m => m.GossipEndPoint != directGossipEndPoint)
                .Select(m => m.GossipEndPoint)
                .Distinct()
                .Take(_numberOfIndirectEndpoints);
        }

        private void HandleSuspiciousMember(IPEndPoint gossipEndPoint)
        {
            lock (_memberLocker)
            {
                if (_members.TryGetValue(gossipEndPoint, out var member) && member.State == MemberState.Alive)
                {
                    member.Update(MemberState.Suspicious);
                    _logger.LogInformation("Gossip.Mesh suspicious member {member}", member);
                    _listener.MemberStateUpdated(member);
                }
            }
        }

        private void HandleDeadMembers()
        {
            lock (_awaitingAcksLock)
            {
                foreach (var awaitingAck in _awaitingAcks.ToArray())
                {
                    // if we havn't recieved an ack before the timeout
                    if (awaitingAck.Value < DateTime.Now)
                    {
                        lock (_memberLocker)
                        {
                            if (_members.TryGetValue(awaitingAck.Key, out var member) && member.State == MemberState.Alive || member.State == MemberState.Suspicious)
                            {
                                member.Update(MemberState.Dead);
                                _awaitingAcks.Remove(awaitingAck.Key);
                                _logger.LogInformation("Gossip.Mesh dead member {member}", member);
                                _listener.MemberStateUpdated(member);
                            }
                        }
                    }
                    // prune dead members
                }
            }
        }

        private void AddAwaitingAck(IPEndPoint gossipEndPoint)
        {
            lock (_awaitingAcksLock)
            {
                if (!_awaitingAcks.ContainsKey(gossipEndPoint))
                {
                    _awaitingAcks.Add(gossipEndPoint, DateTime.Now.AddMilliseconds(_protocolPeriodMs * 5));
                }
            }
        }

        private bool CheckWasNotAcked(IPEndPoint gossipEndPoint)
        {
            var wasNotAcked = false;
            lock (_awaitingAcksLock)
            {
                wasNotAcked = _awaitingAcks.ContainsKey(gossipEndPoint);
            }

            return wasNotAcked;
        }

        private void RemoveAwaitingAck(IPEndPoint gossipEndPoint)
        {
            lock (_awaitingAcksLock)
            {
                if (_awaitingAcks.ContainsKey(gossipEndPoint))
                {
                    _awaitingAcks.Remove(gossipEndPoint);
                }
            }
        }

        private bool EndPointsMatch(IPEndPoint ipEndPointA, IPEndPoint ipEndPointB)
        {
            return ipEndPointA.Port == ipEndPointB.Port && ipEndPointA.Address.Equals(ipEndPointB.Address);
        }

        private async Task WaitForProtocolPeriod()
        {
            var syncTime = _protocolPeriodMs - (int)(DateTime.Now - _lastProtocolPeriod).TotalMilliseconds;
            await Task.Delay(syncTime).ConfigureAwait(false);
            _lastProtocolPeriod = DateTime.Now;
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