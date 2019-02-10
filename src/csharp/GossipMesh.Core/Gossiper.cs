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
        private readonly GossiperOptions _options;
        private readonly IEnumerable<IMemberEventListener> _memberEventListeners;
        private readonly object _memberLocker = new Object();
        private readonly Dictionary<IPEndPoint, Member> _members = new Dictionary<IPEndPoint, Member>();
        private readonly object _awaitingAcksLocker = new Object();
        private volatile Dictionary<IPEndPoint, DateTime> _awaitingAcks = new Dictionary<IPEndPoint, DateTime>();
        private readonly object _pruneMembersLocker = new Object();
        private volatile Dictionary<IPEndPoint, DateTime> _pruneMembers = new Dictionary<IPEndPoint, DateTime>();
        private DateTime _lastProtocolPeriod = DateTime.Now;
        private readonly Random _rand = new Random();
        private UdpClient _udpClient;

        private readonly ILogger _logger;

        public Gossiper(GossiperOptions options, IEnumerable<IMemberEventListener> memberEventListeners, ILogger logger)
        {
            _options = options;
            _memberEventListeners = memberEventListeners;
            _logger = logger;

            _self = new Member(MemberState.Alive, options.MemberIP, options.ListenPort, 1, options.Service, options.ServicePort);
        }

        public void Start()
        {
            _logger.LogInformation("Starting Gossip.Mesh Gossiper on {GossipEndPoint}", _self.GossipEndPoint);
            PushToMemberEventListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));

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
                        var messageType = stream.ReadMessageType();
                        _logger.LogDebug("Gossip.Mesh recieved {MessageType} from {RemoteEndPoint}", messageType, request.RemoteEndPoint);

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
                        UpdateMembers(request.RemoteEndPoint, receivedDateTime, stream);

                        var members = GetMembers(request.RemoteEndPoint);
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
                        await PingAsync(member.GossipEndPoint, members).ConfigureAwait(false);

                        await Task.Delay(_options.AckTimeoutMilliseconds).ConfigureAwait(false);

                        // check was not acked
                        if (WasNotAcked(member.GossipEndPoint))
                        {
                            var indirectEndpoints = GetIndirectGossipEndPoints(member.GossipEndPoint, members);

                            await PingRequestAsync(member.GossipEndPoint, indirectEndpoints, members).ConfigureAwait(false);

                            await Task.Delay(_options.AckTimeoutMilliseconds).ConfigureAwait(false);

                            if (WasNotAcked(member.GossipEndPoint))
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
                await AckAsync(sourceGossipEndPoint, members).ConfigureAwait(false);
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
                    await AckRequestAsync(sourceGossipEndPoint, request.RemoteEndPoint, members).ConfigureAwait(false);
                }

                // otherwise forward the request
                else
                {
                    await ForwardMessageAsync(destinationGossipEndPoint, sourceGossipEndPoint, request.Buffer).ConfigureAwait(false);
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
                    await ForwardMessageAsync(destinationGossipEndPoint, sourceGossipEndPoint, request.Buffer).ConfigureAwait(false);
                }
            }
        }

        private async Task SendMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint, Member[] members)
        {
            _logger.LogDebug("Gossip.Mesh sending {messageType} to {destinationGossipEndPoint}", messageType, destinationGossipEndPoint);

            using (var stream = new MemoryStream(_options.MaxUdpPacketBytes))
            {
                stream.WriteByte((byte)messageType);
                WriteMembers(stream, members);

                await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, destinationGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task SendMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint, IPEndPoint indirectGossipEndPoint, Member[] members)
        {
            _logger.LogDebug("Gossip.Mesh sending {MessageType} to {destinationGossipEndPoint} via {indirectEndpoint}", messageType, destinationGossipEndPoint, indirectGossipEndPoint);

            using (var stream = new MemoryStream(_options.MaxUdpPacketBytes))
            {
                stream.WriteByte((byte)messageType);

                stream.WriteIPEndPoint(destinationGossipEndPoint);
                stream.WriteIPEndPoint(_self.GossipEndPoint);

                WriteMembers(stream, members);

                await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, indirectGossipEndPoint).ConfigureAwait(false);
            }
        }

        private async Task ForwardMessageAsync(IPEndPoint destinationGossipEndPoint, IPEndPoint sourceGossipEndPoint, byte[] request)
        {
            var messageType = (MessageType)request[0];
            _logger.LogDebug("Gossip.Mesh forwarding {messageType} to {destinationGossipEndPoint} from {sourceGossipEndPoint}", messageType, destinationGossipEndPoint, sourceGossipEndPoint);

            await _udpClient.SendAsync(request, request.Length, destinationGossipEndPoint).ConfigureAwait(false);
        }

        private async Task PingAsync(IPEndPoint destinationGossipEndPoint, Member[] members = null)
        {
            await SendMessageAsync(MessageType.Ping, destinationGossipEndPoint, members).ConfigureAwait(false);
        }

        private async Task AckAsync(IPEndPoint destinationGossipEndPoint, Member[] members)
        {
            await SendMessageAsync(MessageType.Ack, destinationGossipEndPoint, members).ConfigureAwait(false);
        }

        private async Task PingRequestAsync(IPEndPoint destinationGossipEndPoint, IEnumerable<IPEndPoint> indirectGossipEndPoints, Member[] members)
        {
            foreach (var indirectGossipEndPoint in indirectGossipEndPoints)
            {
                await SendMessageAsync(MessageType.PingRequest, destinationGossipEndPoint, indirectGossipEndPoint, members).ConfigureAwait(false);
            }
        }

        private async Task AckRequestAsync(IPEndPoint destinationGossipEndPoint, IPEndPoint indirectGossipEndPoint, Member[] members)
        {
            await SendMessageAsync(MessageType.AckRequest, destinationGossipEndPoint, indirectGossipEndPoint, members).ConfigureAwait(false);
        }

        private void UpdateMembers(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Stream stream)
        {
            while (stream.Position < stream.Length)
            {
                var memberEvent = MemberEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream);
                PushToMemberEventListeners(memberEvent);

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

                            PushToMemberEventListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member));
                            //PushToMemberUpdatedListeners(memberEvent);
                        }

                        else if (member == null)
                        {
                            member = new Member(memberEvent);
                            _members.Add(member.GossipEndPoint, member);
                            _logger.LogInformation("Gossip.Mesh member added {member}", member);

                            PushToMemberEventListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member));
                            //PushToMemberUpdatedListeners(memberEvent);
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

                    PushToMemberEventListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));
                }
            }
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

        private Member[] GetMembers(IPEndPoint destinationGossipEndPoint = null)
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

        private IEnumerable<IPEndPoint> GetIndirectGossipEndPoints(IPEndPoint directGossipEndPoint, Member[] members)
        {
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

                    PushToMemberEventListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member));
                    //PushToMemberUpdatedListeners(new MemberEvent(member));
                }
            }
        }

        private void HandleDeadMembers()
        {
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

                                PushToMemberEventListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member));
                                //PushToMemberUpdatedListeners(new MemberEvent(member));
                            }
                        }

                        AddPruneMember(awaitingAck.Key);
                        _awaitingAcks.Remove(awaitingAck.Key);
                    }
                }
            }

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

        private void AddPruneMember(IPEndPoint gossipEndPoint)
        {
            lock (_pruneMembersLocker)
            {
                if (!_pruneMembers.ContainsKey(gossipEndPoint))
                {
                    _pruneMembers.Add(gossipEndPoint, DateTime.Now.AddMilliseconds(_options.ProtocolPeriodMilliseconds * 60));
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
            var syncTime = _options.ProtocolPeriodMilliseconds - (int)(DateTime.Now - _lastProtocolPeriod).TotalMilliseconds;
            await Task.Delay(syncTime).ConfigureAwait(false);
            _lastProtocolPeriod = DateTime.Now;
        }

        private void PushToMemberEventListeners(MemberEvent memberEvent)
        {
            Task.Run(() =>
            {
                foreach (var listener in _memberEventListeners)
                {
                    listener.MemberEventCallback(memberEvent);
                }
            });
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