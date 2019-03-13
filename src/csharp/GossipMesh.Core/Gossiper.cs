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
        private DateTime _lastProtocolPeriod = DateTime.Now;
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
            Task.Run(async () => await GossipPump().ConfigureAwait(false)).ConfigureAwait(false);

            // prune
            Task.Run(async () => await Prune().ConfigureAwait(false)).ConfigureAwait(false);
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

        private async Task GossipPump()
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
                // check was not acked
                if (WasNotAcked(gossipEndPoint))
                {
                    _logger.LogDebug("in here with endpoint {gossipEndPoint}", gossipEndPoint);
                    UpdateMemberState(gossipEndPoint, MemberState.Suspicious);
                    var indirectEndpoints = GetRandomGossipEndPoints(_options.NumberOfIndirectEndpoints, gossipEndPoint);

                    await RequestPingAsync(gossipEndPoint, indirectEndpoints).ConfigureAwait(false);

                    await Task.Delay(_options.AckTimeoutMilliseconds).ConfigureAwait(false);

                    if (WasNotAcked(gossipEndPoint))
                    {
                        UpdateMemberState(gossipEndPoint, MemberState.Dead);
                    }
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
            }
        }

        private async Task Prune()
        {
            while (true)
            {
                lock (_locker)
                {
                    foreach (var awaitingAck in _awaitingAcks.ToArray())
                    {
                        if (DateTime.Now > awaitingAck.Value.AddMilliseconds(_options.ProtocolPeriodMilliseconds * 600))
                        {
                            lock (_locker)
                            {
                                if (_members.TryGetValue(awaitingAck.Key, out var member) && (member.State == MemberState.Dead || member.State == MemberState.Left))
                                {
                                    _members.Remove(awaitingAck.Key);
                                    _logger.LogInformation("Gossip.Mesh pruned member {member}", member);
                                }
                            }

                            _awaitingAcks.Remove(awaitingAck.Key);
                        }
                    }
                }

                await Task.Delay(10000).ConfigureAwait(false);
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
            var memberEvents = new List<MemberEvent>();

            // read sender
            var memberEvent = MemberEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream, true);

            // handle ourself
            var selfState = stream.ReadMemberState();
            var selfGeneration = (byte)stream.ReadByte();

            if (_self.IsLaterGeneration(selfGeneration) ||
                (selfState != MemberState.Alive && selfGeneration == _self.Generation))
            {
                _self.Generation = (byte)(selfGeneration + 1);
                _logger.LogInformation("Gossip.Mesh received a memberEvent: {memberEvent} about self. Upped generation: {generation}", memberEvent, _self.Generation);

                memberEvents.Add(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));
            }

            // handler sender and everyone else
            while (memberEvent != null)
            {
                memberEvents.Add(memberEvent);

                lock (_locker)
                {
                    if (_members.TryGetValue(memberEvent.GossipEndPoint, out var member) &&
                        (member.IsLaterGeneration(memberEvent.Generation) ||
                        (member.Generation == memberEvent.Generation && member.IsStateSuperseded(memberEvent.State))))
                    {
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

                    if (member.State != MemberState.Alive)
                    {
                        AddAwaitingAck(member.GossipEndPoint);
                    }
                }

                memberEvent = MemberEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream);
            }

            PushToMemberEventsListeners(memberEvents);
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
                    .Where(m => !(_awaitingAcks.TryGetValue(m.GossipEndPoint, out var t) && DateTime.Now > t.AddMilliseconds(_options.ProtocolPeriodMilliseconds * 300)) &&
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
                    PushToMemberEventsListeners(new MemberEvent[] { memberEvent });
                }
            }
        }

        private void AddAwaitingAck(IPEndPoint gossipEndPoint)
        {
            lock (_locker)
            {
                if (!_awaitingAcks.ContainsKey(gossipEndPoint))
                {
                    _awaitingAcks.Add(gossipEndPoint, DateTime.Now);
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
            var syncTime = Math.Max(_options.ProtocolPeriodMilliseconds - (int)(DateTime.Now - _lastProtocolPeriod).TotalMilliseconds, 0);
            await Task.Delay(syncTime).ConfigureAwait(false);
            _lastProtocolPeriod = DateTime.Now;
        }

        private void PushToMemberEventsListeners(IEnumerable<MemberEvent> memberEvents)
        {
            foreach (var listener in _options.MemberEventsListeners)
            {
                listener.MemberEventsCallback(memberEvents).ConfigureAwait(false);
            }
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