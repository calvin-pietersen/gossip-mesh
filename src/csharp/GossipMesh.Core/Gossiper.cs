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
        private readonly byte _protocolVersion = 0x00;
        private readonly Member _self;
        private readonly GossiperOptions _options;
        private readonly IEnumerable<IMemberEventsListener> _memberEventsListeners;
        private readonly IEnumerable<IMemberListener> _memberListeners;
        private readonly object _memberLocker = new Object();
        private readonly Dictionary<IPEndPoint, Member> _members = new Dictionary<IPEndPoint, Member>();
        private readonly object _awaitingAcksLocker = new Object();
        private volatile Dictionary<IPEndPoint, DateTime> _awaitingAcks = new Dictionary<IPEndPoint, DateTime>();
        private readonly object _deadMembersLocker = new Object();
        private volatile Dictionary<IPEndPoint, DateTime> _deadMembers = new Dictionary<IPEndPoint, DateTime>();
        private readonly object _pruneMembersLocker = new Object();
        private volatile Dictionary<IPEndPoint, DateTime> _pruneMembers = new Dictionary<IPEndPoint, DateTime>();
        private DateTime _lastProtocolPeriod = DateTime.Now;
        private readonly Random _rand = new Random();
        private UdpClient _udpClient;

        private readonly ILogger _logger;

        public Gossiper(GossiperOptions options, IEnumerable<IMemberEventsListener> memberEventsListeners, IEnumerable<IMemberListener> memberListeners, ILogger logger)
        {
            _options = options;
            _memberEventsListeners = memberEventsListeners;
            _memberListeners = memberListeners;
            _logger = logger;

            _self = new Member(MemberState.Alive, options.MemberIP, options.ListenPort, 1, options.Service, options.ServicePort);
        }

        public void Start()
        {
            _logger.LogInformation("Gossip.Mesh starting Gossiper on {GossipEndPoint}", _self.GossipEndPoint);

            PushToMemberListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));

            _udpClient = CreateUdpClient(_self.GossipEndPoint);

            // bootstrap
            Task.Run(async () => await Bootstraper().ConfigureAwait(false)).ConfigureAwait(false);

            // receive requests
            Task.Run(async () => await Listener().ConfigureAwait(false)).ConfigureAwait(false);

            // gossip
            Task.Run(async () => await Gossip().ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task Bootstraper()
        {
            if (_options.SeedMembers?.Length > 0)
            {
                _logger.LogInformation("Gossip.Mesh bootstrapping off seeds");

                // ideally we want to bootstap over tcp but for now we will ping seeds and stop bootstrapping when we get back some members
                while (IsMembersEmpty())
                {
                    try
                    {
                        // ping seed
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
                              _protocolVersion,
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

        private async Task Gossip()
        {
            while (true)
            {
                try
                {
                    if (TryGetRandomGossipEndPoint(out var gossipEndPoint))
                    {
                        AddAwaitingAck(gossipEndPoint);
                        await PingAsync(gossipEndPoint).ConfigureAwait(false);

                        await Task.Delay(_options.AckTimeoutMilliseconds).ConfigureAwait(false);

                        // check was not acked
                        if (WasNotAcked(gossipEndPoint))
                        {
                            var indirectEndpoints = GetRandomIndirectGossipEndPoints(gossipEndPoint);

                            await RequestPingAsync(gossipEndPoint, indirectEndpoints).ConfigureAwait(false);

                            await Task.Delay(_options.AckTimeoutMilliseconds).ConfigureAwait(false);

                            if (WasNotAcked(gossipEndPoint))
                            {
                                HandleSuspiciousMember(gossipEndPoint);
                            }
                        }
                    }

                    HandleAckTimeOutMembers();
                    HandleDeadMembers();
                    HandlePruneMembers();
                }

                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
                }

                await WaitForProtocolPeriod().ConfigureAwait(false);
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
            
            var members = GetMembers(destinationGossipEndPoint);
            using (var stream = new MemoryStream(_options.MaxUdpPacketBytes))
            {
                stream.WriteByte(_protocolVersion);
                stream.WriteByte((byte)messageType);
                WriteMembers(stream, members);

                await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, destinationGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task RequestMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint, IPEndPoint indirectGossipEndPoint)
        {
            _logger.LogDebug("Gossip.Mesh sending {messageType} to {destinationGossipEndPoint} via {indirectEndpoint}", messageType, destinationGossipEndPoint, indirectGossipEndPoint);

            var members = GetMembers(destinationGossipEndPoint);
            using (var stream = new MemoryStream(_options.MaxUdpPacketBytes))
            {
                stream.WriteByte(_protocolVersion);
                stream.WriteByte((byte)messageType);
                stream.WriteIPEndPoint(destinationGossipEndPoint);
                WriteMembers(stream, members);

                await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, indirectGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task ForwardMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint, IPEndPoint sourceGossipEndPoint)
        {
            _logger.LogDebug("Gossip.Mesh sending {messageType} to {destinationGossipEndPoint} from {sourceGossipEndPoint}", messageType, destinationGossipEndPoint, sourceGossipEndPoint);

            var members = GetMembers(destinationGossipEndPoint);
            using (var stream = new MemoryStream(_options.MaxUdpPacketBytes))
            {
                stream.WriteByte(_protocolVersion);
                stream.WriteByte((byte)messageType);
                stream.WriteIPEndPoint(sourceGossipEndPoint);
                WriteMembers(stream, members);

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
            var memberEvents = new List<MemberEvent>();
            while (stream.Position < stream.Length)
            {
                var memberEvent = MemberEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream);
                memberEvents.Add(memberEvent);

                // we don't add ourselves to the member list
                if (!EndPointsMatch(memberEvent.GossipEndPoint, _self.GossipEndPoint))
                {
                    lock (_memberLocker)
                    {
                        if (_members.TryGetValue(memberEvent.GossipEndPoint, out var member) &&
                            (member.IsLaterGeneration(memberEvent.Generation) ||
                            (member.Generation == memberEvent.Generation && member.IsStateSuperseded(memberEvent.State))))
                        {
                            RemoveAwaitingAck(member.GossipEndPoint); // stops dead claim escalation
                            member.Update(memberEvent);
                            _logger.LogInformation("Gossip.Mesh member state changed {member}", member);

                            var selfMemberEvent = new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member);
                            PushToMemberListeners(selfMemberEvent);
                            memberEvents.Add(selfMemberEvent);
                        }

                        else if (member == null)
                        {
                            member = new Member(memberEvent);
                            _members.Add(member.GossipEndPoint, member);
                            _logger.LogInformation("Gossip.Mesh member added {member}", member);

                            var selfMemberEvent = new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member);
                            PushToMemberListeners(selfMemberEvent);
                            memberEvents.Add(selfMemberEvent);
                        }
                    }

                    if (memberEvent.State == MemberState.Dead || memberEvent.State == MemberState.Left)
                    {
                        AddPruneMember(memberEvent.GossipEndPoint);
                    }
                }

                else
                {
                    // handle any state claims about ourselves
                    if (_self.IsLaterGeneration(memberEvent.Generation) ||
                        (memberEvent.State != MemberState.Alive && memberEvent.Generation == _self.Generation))
                    {
                        _self.Generation = (byte)(memberEvent.Generation + 1);
                        _logger.LogInformation("Gossip.Mesh received a memberEvent: {memberEvent} about self. Upped generation: {generation}", memberEvent, _self.Generation);
                    }

                    memberEvents.Add(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));
                }
            }

            PushToMemberEventsListeners(memberEvents);
        }

        private void WriteMembers(Stream stream, Member[] members)
        {
            // always prioritize ourselves
            _self.WriteTo(stream);

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
            lock (_pruneMembersLocker)
            {
                lock (_memberLocker)
                {
                    return _members
                        .Values
                        .OrderBy(m => m.GossipCounter)
                        .Where(m => !_pruneMembers.ContainsKey(m.GossipEndPoint) || EndPointsMatch(destinationGossipEndPoint, m.GossipEndPoint))
                        .ToArray();
                }
            }
        }

        private bool IsMembersEmpty()
        {
            lock (_memberLocker)
            {
                return _members.Count == 0;
            }
        }

        private bool TryGetRandomGossipEndPoint(out IPEndPoint gossipEndPoint)
        {
            var success = false;
            gossipEndPoint = null;

            lock (_memberLocker)
            {
                if (_members.Any())
                {
                    gossipEndPoint = _members.Values.ElementAt(_rand.Next(0, _members.Count())).GossipEndPoint;
                    success = true;
                }
            }

            return success;
        }

        private IEnumerable<IPEndPoint> GetRandomIndirectGossipEndPoints(IPEndPoint directGossipEndPoint)
        {
            var members = GetMembers(directGossipEndPoint);

            if (members.Length <= 1)
            {
                return Enumerable.Empty<IPEndPoint>();
            }

            var randomIndex = _rand.Next(0, members.Length);

            return Enumerable.Range(randomIndex, _options.NumberOfIndirectEndpoints + 1)
                .Select(ri => ri % members.Length) // wrap the range around to 0 if we hit the end
                .Select(i => members[i])
                .Where(m => m.GossipEndPoint != directGossipEndPoint)
                .Select(m => m.GossipEndPoint)
                .Distinct()
                .Take(_options.NumberOfIndirectEndpoints);
        }

        private void HandleSuspiciousMember(IPEndPoint gossipEndPoint)
        {
            lock (_memberLocker)
            {
                if (_members.TryGetValue(gossipEndPoint, out var member) && member.State == MemberState.Alive)
                {
                    member.Update(MemberState.Suspicious);
                    _logger.LogInformation("Gossip.Mesh suspicious member {member}", member);

                    var memberEvent = new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member);
                    PushToMemberListeners(memberEvent);
                    PushToMemberEventsListeners(new MemberEvent[] { memberEvent });
                }
            }
        }

        private void HandleAckTimeOutMembers()
        {
            var memberEvents = new List<MemberEvent>();
            lock (_awaitingAcksLocker)
            {
                foreach (var awaitingAck in _awaitingAcks.ToArray())
                {
                    // if we havn't recieved an ack before the timeout
                    if (awaitingAck.Value < DateTime.Now)
                    {
                        lock (_memberLocker)
                        {
                            if (_members.TryGetValue(awaitingAck.Key, out var member) && (member.State == MemberState.Alive || member.State == MemberState.Suspicious))
                            {
                                member.Update(MemberState.Dead);
                                _logger.LogInformation("Gossip.Mesh dead member {member}", member);

                                var memberEvent = new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member);
                                PushToMemberListeners(memberEvent);
                                memberEvents.Add(memberEvent);
                            }
                        }

                        AddDeadMember(awaitingAck.Key);
                        _awaitingAcks.Remove(awaitingAck.Key);
                    }
                }
            }

            PushToMemberEventsListeners(memberEvents);
        }

        private void HandleDeadMembers()
        {
            lock (_deadMembersLocker)
            {
                foreach (var deadMember in _deadMembers.ToArray())
                {
                    if (deadMember.Value < DateTime.Now)
                    {
                        lock (_memberLocker)
                        {
                            if (_members.TryGetValue(deadMember.Key, out var member) && (member.State == MemberState.Dead || member.State == MemberState.Left))
                            {
                                AddPruneMember(deadMember.Key);
                                _logger.LogInformation("Gossip.Mesh dead member moved for pruning {member}", member);
                            }
                        }

                        _deadMembers.Remove(deadMember.Key);
                    }
                }
            }
        }

        private void HandlePruneMembers()
        {
            lock (_pruneMembersLocker)
            {
                foreach (var pruneMember in _pruneMembers.ToArray())
                {
                    if (pruneMember.Value < DateTime.Now)
                    {
                        lock (_memberLocker)
                        {
                            if (_members.TryGetValue(pruneMember.Key, out var member) && (member.State == MemberState.Dead || member.State == MemberState.Left))
                            {
                                _members.Remove(pruneMember.Key);
                                _logger.LogInformation("Gossip.Mesh pruned member {member}", member);
                            }
                        }

                        _pruneMembers.Remove(pruneMember.Key);
                    }
                }
            }
        }

        private void AddAwaitingAck(IPEndPoint gossipEndPoint)
        {
            lock (_awaitingAcksLocker)
            {
                if (!_awaitingAcks.ContainsKey(gossipEndPoint))
                {
                    _awaitingAcks.Add(gossipEndPoint, DateTime.Now.AddMilliseconds(_options.ProtocolPeriodMilliseconds * 5));
                }
            }
        }

        private void AddDeadMember(IPEndPoint gossipEndPoint)
        {
            lock (_deadMembersLocker)
            {
                if (!_deadMembers.ContainsKey(gossipEndPoint))
                {
                    _deadMembers.Add(gossipEndPoint, DateTime.Now.AddMilliseconds(_options.ProtocolPeriodMilliseconds * 240));
                }
            }
        }

        private void AddPruneMember(IPEndPoint gossipEndPoint)
        {
            lock (_pruneMembersLocker)
            {
                if (!_pruneMembers.ContainsKey(gossipEndPoint))
                {
                    _pruneMembers.Add(gossipEndPoint, DateTime.Now.AddMilliseconds(_options.ProtocolPeriodMilliseconds * 600));
                }
            }
        }

        private bool WasNotAcked(IPEndPoint gossipEndPoint)
        {
            var wasNotAcked = false;
            lock (_awaitingAcksLocker)
            {
                wasNotAcked = _awaitingAcks.ContainsKey(gossipEndPoint);
            }

            return wasNotAcked;
        }

        private void RemoveAwaitingAck(IPEndPoint gossipEndPoint)
        {
            lock (_awaitingAcksLocker)
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
            var syncTime = Math.Max(_options.ProtocolPeriodMilliseconds - (int)(DateTime.Now - _lastProtocolPeriod).TotalMilliseconds, 0);
            await Task.Delay(syncTime).ConfigureAwait(false);
            _lastProtocolPeriod = DateTime.Now;
        }

        private void PushToMemberEventsListeners(IEnumerable<MemberEvent> memberEvents)
        {
            foreach (var listener in _memberEventsListeners)
            {
                listener.MemberEventsCallback(memberEvents).ConfigureAwait(false);
            }
        }

        private void PushToMemberListeners(MemberEvent memberEvent)
        {
            foreach (var listener in _memberListeners)
            {
                listener.MemberUpdatedCallback(memberEvent).ConfigureAwait(false);
            }
        }

        private bool IsVersionCompatible(byte version)
        {
            // can add more complex mapping for backwards compatibility
            return _protocolVersion == version;
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