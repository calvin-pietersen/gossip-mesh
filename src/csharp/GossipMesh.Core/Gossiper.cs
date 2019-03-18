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
        private const byte PROTOCOL_VERSION = 0x00;
        private readonly object _locker = new Object();
        private readonly Member _self;
        private readonly Dictionary<IPEndPoint, Member> _members = new Dictionary<IPEndPoint, Member>();
        private volatile Dictionary<IPEndPoint, DateTime> _awaitingAcks = new Dictionary<IPEndPoint, DateTime>();
        private DateTime _lastProtocolPeriod = DateTime.UtcNow;
        private readonly Random _rand = new Random();
        private UdpClient _udpClient;

        private readonly GossiperOptions _options;
        private readonly ILogger _logger;

        public Gossiper(ushort listenPort, byte service, ushort servicePort, GossiperOptions options, ILogger logger)
        {
            _options = options;
            _logger = logger;

            _self = new Member(MemberState.Alive, IPAddress.Any, listenPort, 1, service, servicePort);
        }

        public async Task StartAsync()
        {
            _logger.LogInformation("Gossip.Mesh starting Gossiper on {GossipEndPoint}", _self.GossipEndPoint);
            _udpClient = CreateUdpClient(_self.GossipEndPoint);
            PushToMemberListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));

            // start listener
            Listener();

            // bootstrap off seeds
            await Bootstraper().ConfigureAwait(false);

            // gossip
            GossipPump();

            // detect dead members and prune old members
            DeadMemberHandler();
        }

        private async Task Bootstraper()
        {
            if (_options.SeedMembers == null || _options.SeedMembers.Length == 0)
            {
                _logger.LogInformation("Gossip.Mesh no seeds to bootstrap off");
                return;
            }

            _logger.LogInformation("Gossip.Mesh bootstrapping off seeds");

            while (IsMembersEmpty())
            {
                try
                {
                    var i = _rand.Next(0, _options.SeedMembers.Length);
                    await PingAsync(_options.SeedMembers[i]).ConfigureAwait(false);
                }

                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
                }

                await Task.Delay(_options.ProtocolPeriodMilliseconds).ConfigureAwait(false);
            }

            _logger.LogInformation("Gossip.Mesh finished bootstrapping");
        }

        private async void Listener()
        {
            while (true)
            {
                try
                {
                    var request = await _udpClient.ReceiveAsync().ConfigureAwait(false);
                    var receivedDateTime = DateTime.UtcNow;

                    using (var stream = new MemoryStream(request.Buffer, false))
                    {
                        var remoteProtocolVersion = stream.ReadByte();
                        if (IsVersionCompatible(request.Buffer[0]))
                        {
                            var messageType = stream.ReadMessageType();
                            _logger.LogDebug("Gossip.Mesh received {MessageType} from {RemoteEndPoint}", messageType, request.RemoteEndPoint);

                            IPEndPoint destinationGossipEndPoint;
                            IPEndPoint sourceGossipEndPoint;

                            if (messageType == MessageType.Ping || messageType == MessageType.Ack)
                            {
                                sourceGossipEndPoint = request.RemoteEndPoint;
                                destinationGossipEndPoint = _self.GossipEndPoint;
                            }

                            else if (messageType == MessageType.RequestPing || messageType == MessageType.RequestAck)
                            {
                                sourceGossipEndPoint = request.RemoteEndPoint;
                                destinationGossipEndPoint = stream.ReadIPEndPoint();
                            }

                            else
                            {
                                sourceGossipEndPoint = stream.ReadIPEndPoint();
                                destinationGossipEndPoint = _self.GossipEndPoint;
                            }

                            UpdateMembers(request.RemoteEndPoint, receivedDateTime, stream);
                            await RequestHandler(request, messageType, sourceGossipEndPoint, destinationGossipEndPoint).ConfigureAwait(false);
                        }

                        else
                        {
                            _logger.LogInformation("Gossip.Mesh received message on incompatible protocol version from {RemoteEndPoint}. Current version: {CurrentVersion} Received version: {ReceivedVersion}",
                             request.RemoteEndPoint,
                              PROTOCOL_VERSION,
                              remoteProtocolVersion);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
                }
            }
        }

        private async void GossipPump()
        {
            while (true)
            {
                try
                {
                    var gossipEndPoints = GetRandomGossipEndPoints(_options.FanoutFactor).ToArray();

                    var gossipTasks = new Task[gossipEndPoints.Length];

                    for (int i = 0; i < gossipEndPoints.Length; i++)
                    {
                        gossipTasks[i] = Gossip(gossipEndPoints[i]);
                    }

                    await Task.WhenAll(gossipTasks).ConfigureAwait(false);

                    await WaitForProtocolPeriod().ConfigureAwait(false);
                }

                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
                }
            }
        }

        private async Task Gossip(IPEndPoint gossipEndPoint)
        {
            try
            {
                AddAwaitingAck(gossipEndPoint);
                await PingAsync(gossipEndPoint).ConfigureAwait(false);

                await Task.Delay(_options.AckTimeoutMilliseconds).ConfigureAwait(false);

                if (WasNotAcked(gossipEndPoint))
                {
                    var indirectEndpoints = GetRandomGossipEndPoints(_options.NumberOfIndirectEndpoints, gossipEndPoint);
                    await RequestPingAsync(gossipEndPoint, indirectEndpoints).ConfigureAwait(false);

                    await Task.Delay(_options.AckTimeoutMilliseconds).ConfigureAwait(false);
                }

                if (WasNotAcked(gossipEndPoint))
                {
                    UpdateMemberState(gossipEndPoint, MemberState.Suspicious);
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
            }
        }

        private async void DeadMemberHandler()
        {
            while (true)
            {
                try
                {
                    lock (_locker)
                    {
                        foreach (var awaitingAck in _awaitingAcks.ToArray())
                        {
                            if (DateTime.UtcNow > awaitingAck.Value.AddMilliseconds(_options.PruneTimeoutMilliseconds))
                            {
                                if (_members.TryGetValue(awaitingAck.Key, out var member) && (member.State == MemberState.Dead || member.State == MemberState.Left))
                                {
                                    _members.Remove(awaitingAck.Key);
                                    _logger.LogInformation("Gossip.Mesh pruned member {member}", member);

                                    member.Update(MemberState.Pruned);
                                    PushToMemberListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member));
                                }

                                _awaitingAcks.Remove(awaitingAck.Key);
                            }

                            else if (DateTime.UtcNow > awaitingAck.Value.AddMilliseconds(_options.DeadTimeoutMilliseconds))
                            {
                                UpdateMemberState(awaitingAck.Key, MemberState.Dead);
                            }
                        }
                    }
                }

                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        private async Task RequestHandler(UdpReceiveResult request, MessageType messageType, IPEndPoint sourceGossipEndPoint, IPEndPoint destinationGossipEndPoint)
        {
            if (messageType == MessageType.Ping)
            {
                await AckAsync(sourceGossipEndPoint).ConfigureAwait(false);
            }

            else if (messageType == MessageType.Ack)
            {
                RemoveAwaitingAck(sourceGossipEndPoint);
            }

            else if (messageType == MessageType.RequestPing)
            {
                await ForwardMessageAsync(MessageType.ForwardedPing, destinationGossipEndPoint, sourceGossipEndPoint).ConfigureAwait(false);
            }

            else if (messageType == MessageType.RequestAck)
            {
                await ForwardMessageAsync(MessageType.ForwardedAck, destinationGossipEndPoint, sourceGossipEndPoint).ConfigureAwait(false);
            }

            else if (messageType == MessageType.ForwardedPing)
            {
                await RequestAckAsync(sourceGossipEndPoint, request.RemoteEndPoint).ConfigureAwait(false);
            }

            else if (messageType == MessageType.ForwardedAck)
            {
                RemoveAwaitingAck(sourceGossipEndPoint);
            }
        }

        private async Task SendMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint)
        {
            _logger.LogDebug("Gossip.Mesh sending {messageType} to {destinationGossipEndPoint}", messageType, destinationGossipEndPoint);
            
            using (var stream = new MemoryStream(_options.MaxUdpPacketBytes))
            {
                stream.WriteByte(PROTOCOL_VERSION);
                stream.WriteByte((byte)messageType);
                WriteMembers(stream, destinationGossipEndPoint);

                await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, destinationGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task RequestMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint, IPEndPoint indirectGossipEndPoint)
        {
            _logger.LogDebug("Gossip.Mesh sending {messageType} to {destinationGossipEndPoint} via {indirectEndpoint}", messageType, destinationGossipEndPoint, indirectGossipEndPoint);

            using (var stream = new MemoryStream(_options.MaxUdpPacketBytes))
            {
                stream.WriteByte(PROTOCOL_VERSION);
                stream.WriteByte((byte)messageType);
                stream.WriteIPEndPoint(destinationGossipEndPoint);
                WriteMembers(stream, indirectGossipEndPoint);

                await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, indirectGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task ForwardMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint, IPEndPoint sourceGossipEndPoint)
        {
            _logger.LogDebug("Gossip.Mesh sending {messageType} to {destinationGossipEndPoint} from {sourceGossipEndPoint}", messageType, destinationGossipEndPoint, sourceGossipEndPoint);

            using (var stream = new MemoryStream(_options.MaxUdpPacketBytes))
            {
                stream.WriteByte(PROTOCOL_VERSION);
                stream.WriteByte((byte)messageType);
                stream.WriteIPEndPoint(sourceGossipEndPoint);
                WriteMembers(stream, destinationGossipEndPoint);

                await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, destinationGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task PingAsync(IPEndPoint destinationGossipEndPoint)
        {
            await SendMessageAsync(MessageType.Ping, destinationGossipEndPoint).ConfigureAwait(false);
        }

        private async Task AckAsync(IPEndPoint destinationGossipEndPoint)
        {
            await SendMessageAsync(MessageType.Ack, destinationGossipEndPoint).ConfigureAwait(false);
        }

        private async Task RequestPingAsync(IPEndPoint destinationGossipEndPoint, IEnumerable<IPEndPoint> indirectGossipEndPoints)
        {
            foreach (var indirectGossipEndPoint in indirectGossipEndPoints)
            {
                await RequestMessageAsync(MessageType.RequestPing, destinationGossipEndPoint, indirectGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task RequestAckAsync(IPEndPoint destinationGossipEndPoint, IPEndPoint indirectGossipEndPoint)
        {
            await RequestMessageAsync(MessageType.RequestAck, destinationGossipEndPoint, indirectGossipEndPoint).ConfigureAwait(false);
        }

        private void UpdateMembers(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Stream stream)
        {
            // read sender
            var memberEvent = MemberEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream, true);

            // handle ourself
            var selfClaimedState = stream.ReadMemberState();
            var selfClaimedGeneration = (byte)stream.ReadByte();

            if (_self.IsLaterGeneration(selfClaimedGeneration) ||
                (selfClaimedState != MemberState.Alive && selfClaimedGeneration == _self.Generation))
            {
                PushToMemberListeners(new MemberEvent(senderGossipEndPoint, receivedDateTime, _self.IP, _self.GossipPort, selfClaimedState, selfClaimedGeneration));

                _self.Generation = (byte)(selfClaimedGeneration + 1);
                _logger.LogInformation("Gossip.Mesh received a claim about self, state:{state} generation:{generation}. Raising generation to {generation}", selfClaimedState, selfClaimedGeneration, _self.Generation);

                PushToMemberListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));
            }

            // handler sender and everyone else
            while (memberEvent != null)
            {
                lock (_locker)
                {
                    if (_members.TryGetValue(memberEvent.GossipEndPoint, out var member) &&
                        (member.IsLaterGeneration(memberEvent.Generation) ||
                        (member.Generation == memberEvent.Generation && member.IsStateSuperseded(memberEvent.State))))
                    {
                        // stops state escalation
                        if (memberEvent.State == MemberState.Alive && memberEvent.Generation > member.Generation)
                        {
                            RemoveAwaitingAck(memberEvent.GossipEndPoint);
                        }

                        member.Update(memberEvent);
                        _logger.LogInformation("Gossip.Mesh member state changed {member}", member);

                        PushToMemberListeners(memberEvent);
                    }

                    else if (member == null)
                    {
                        member = new Member(memberEvent);
                        _members.Add(member.GossipEndPoint, member);
                        _logger.LogInformation("Gossip.Mesh member added {member}", member);

                        PushToMemberListeners(memberEvent);
                    }

                    if (member.State != MemberState.Alive)
                    {
                        AddAwaitingAck(member.GossipEndPoint);
                    }
                }

                memberEvent = MemberEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream);
            }
        }

        private void WriteMembers(Stream stream, IPEndPoint destinationGossipEndPoint)
        {
            lock (_locker)
            {
                stream.WriteByte(_self.Generation);
                stream.WriteByte(_self.Service);
                stream.WritePort(_self.ServicePort);

                if (_members.TryGetValue(destinationGossipEndPoint, out var destinationMember))
                {
                    stream.WriteByte((byte)destinationMember.State);
                    stream.WriteByte(destinationMember.Generation);
                }

                else
                {
                    stream.WriteByte((byte)MemberState.Alive);
                    stream.WriteByte(0x01);
                }

                var members = GetMembers(destinationGossipEndPoint);

                if (members != null)
                {
                    var i = 0;
                    {
                        while (i < members.Length && stream.Position < _options.MaxUdpPacketBytes - 11)
                        {
                            var member = members[i];
                            members[i].WriteTo(stream);
                            i++;
                        }
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

        private Member[] GetMembers(IPEndPoint destinationGossipEndPoint)
        {
            lock (_locker)
            {
                return _members
                    .Values
                    .OrderBy(m => m.GossipCounter)
                    .Where(m => !(_awaitingAcks.TryGetValue(m.GossipEndPoint, out var t) && DateTime.UtcNow > t.AddMilliseconds(_options.DeadCoolOffMilliseconds)) &&
                                !EndPointsMatch(destinationGossipEndPoint, m.GossipEndPoint))
                    .ToArray();
            }
        }

        private bool IsMembersEmpty()
        {
            lock (_locker)
            {
                return _members.Count == 0;
            }
        }

        private IEnumerable<IPEndPoint> GetRandomGossipEndPoints(int numberOfEndPoints, IPEndPoint directGossipEndPoint = null)
        {
            var members = GetMembers(directGossipEndPoint);
            var randomIndex = _rand.Next(0, members.Length);

            if (members.Length == 0)
            {
                return Enumerable.Empty<IPEndPoint>();
            }

            return Enumerable.Range(randomIndex, numberOfEndPoints)
                .Select(ri => ri % members.Length) // wrap the range around to 0 if we hit the end
                .Select(i => members[i])
                .Select(m => m.GossipEndPoint)
                .Distinct()
                .Take(numberOfEndPoints);
        }

        private void UpdateMemberState(IPEndPoint gossipEndPoint, MemberState memberState)
        {
            lock (_locker)
            {
                if (_members.TryGetValue(gossipEndPoint, out var member) && member.State < memberState)
                {
                    member.Update(memberState);
                    _logger.LogInformation("Gossip.Mesh {memberState} member {member}", memberState.ToString().ToLower(), member);

                    var memberEvent = new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member);
                    PushToMemberListeners(memberEvent);
                }
            }
        }

        private void AddAwaitingAck(IPEndPoint gossipEndPoint)
        {
            lock (_locker)
            {
                if (!_awaitingAcks.ContainsKey(gossipEndPoint))
                {
                    _awaitingAcks.Add(gossipEndPoint, DateTime.UtcNow);
                }
            }
        }

        private bool WasNotAcked(IPEndPoint gossipEndPoint)
        {
            var wasNotAcked = false;
            lock (_locker)
            {
                wasNotAcked = _awaitingAcks.ContainsKey(gossipEndPoint);
            }

            return wasNotAcked;
        }

        private void RemoveAwaitingAck(IPEndPoint gossipEndPoint)
        {
            lock (_locker)
            {
                if (_awaitingAcks.ContainsKey(gossipEndPoint))
                {
                    _awaitingAcks.Remove(gossipEndPoint);
                }
            }
        }

        private bool EndPointsMatch(IPEndPoint ipEndPointA, IPEndPoint ipEndPointB)
        {
            return ipEndPointA?.Port == ipEndPointB?.Port && ipEndPointA.Address.Equals(ipEndPointB.Address);
        }

        private async Task WaitForProtocolPeriod()
        {
            var syncTime = Math.Max(_options.ProtocolPeriodMilliseconds - (int)(DateTime.UtcNow - _lastProtocolPeriod).TotalMilliseconds, 0);
            await Task.Delay(syncTime).ConfigureAwait(false);
            _lastProtocolPeriod = DateTime.UtcNow;
        }

        private void PushToMemberListeners(MemberEvent memberEvent)
        {
            foreach (var listener in _options.MemberListeners)
            {
                listener.MemberUpdatedCallback(memberEvent).ConfigureAwait(false);
            }
        }

        private bool IsVersionCompatible(byte version)
        {
            // can add more complex mapping for backwards compatibility
            return PROTOCOL_VERSION == version;
        }

        public void Dispose()
        {
            if (_udpClient != null)
            {
                _udpClient.Close();
            }
        }
    }
}