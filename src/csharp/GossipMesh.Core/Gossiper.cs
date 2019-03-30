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
        private readonly Node _self;
        private readonly Dictionary<IPEndPoint, Node> _nodes = new Dictionary<IPEndPoint, Node>();
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

            _self = new Node(NodeState.Alive, IPAddress.Any, listenPort, 0, service, servicePort);
        }

        public async Task StartAsync()
        {
            _logger.LogInformation("Gossip.Mesh starting Gossiper on {GossipEndPoint}", _self.GossipEndPoint);
            _udpClient = CreateUdpClient(_self.GossipEndPoint);
            PushToNodeListeners(new NodeEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));

            // start listener
            Listener();

            // bootstrap off seeds
            await Bootstraper().ConfigureAwait(false);

            // gossip
            GossipPump();

            // detect dead nodes and prune old nodes
            DeadNodeHandler();

            // low frequency ping to seeds to avoid network partitioning
            NetworkPartitionGaurd();
        }

        private async Task PingRandomSeed()
        {
            try
            {
                var i = _rand.Next(0, _options.SeedNodes.Length);
                await PingAsync(_options.SeedNodes[i]).ConfigureAwait(false);
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
            }

        }

        private async Task Bootstraper()
        {
            if (_options.SeedNodes == null || _options.SeedNodes.Length == 0)
            {
                _logger.LogInformation("Gossip.Mesh no seeds to bootstrap off");
                return;
            }

            _logger.LogInformation("Gossip.Mesh bootstrapping off seeds");

            while (IsNodesEmpty())
            {
                await PingRandomSeed().ConfigureAwait(false);
                await Task.Delay(_options.ProtocolPeriodMilliseconds).ConfigureAwait(false);
            }

            _logger.LogInformation("Gossip.Mesh finished bootstrapping");
        }

        private async void NetworkPartitionGaurd()
        {
            if (_options.SeedNodes == null || _options.SeedNodes.Length == 0)
            {
                return;
            }

            while (true)
            {
                var n = 0;
                lock (_locker)
                {
                    n = _nodes.Count() * 1000;
                }

                // wait the max of either 1m or 1s for each node
                await Task.Delay(Math.Max(60000, n)).ConfigureAwait(false);
                await PingRandomSeed().ConfigureAwait(false);
            }
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

                            UpdateNodes(request.RemoteEndPoint, receivedDateTime, stream);
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
                    await PingAsync(gossipEndPoint).ConfigureAwait(false);

                    await Task.Delay(_options.AckTimeoutMilliseconds).ConfigureAwait(false);
                }

                if (WasNotAcked(gossipEndPoint))
                {
                    UpdateNodeState(gossipEndPoint, NodeState.Suspicious);
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message, ex.StackTrace);
            }
        }

        private async void DeadNodeHandler()
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
                                if (_nodes.TryGetValue(awaitingAck.Key, out var node) && (node.State == NodeState.Dead || node.State == NodeState.Left))
                                {
                                    _nodes.Remove(awaitingAck.Key);
                                    _logger.LogInformation("Gossip.Mesh pruned node {node}", node);

                                    node.Update(NodeState.Pruned);
                                    PushToNodeListeners(new NodeEvent(_self.GossipEndPoint, DateTime.UtcNow, node));
                                }

                                _awaitingAcks.Remove(awaitingAck.Key);
                            }

                            else if (DateTime.UtcNow > awaitingAck.Value.AddMilliseconds(_options.DeadTimeoutMilliseconds))
                            {
                                UpdateNodeState(awaitingAck.Key, NodeState.Dead);
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
                WriteNodes(stream, destinationGossipEndPoint);

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
                WriteNodes(stream, indirectGossipEndPoint);

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
                WriteNodes(stream, destinationGossipEndPoint);

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

        private void UpdateNodes(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Stream stream)
        {
            // read sender
            var nodeEvent = NodeEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream, true);

            // handle ourself
            var selfClaimedState = stream.ReadNodeState();
            var selfClaimedGeneration = (byte)stream.ReadByte();

            if (_self.IsLaterGeneration(selfClaimedGeneration) ||
                (selfClaimedState != NodeState.Alive && selfClaimedGeneration == _self.Generation))
            {
                PushToNodeListeners(new NodeEvent(senderGossipEndPoint, receivedDateTime, _self.IP, _self.GossipPort, selfClaimedState, selfClaimedGeneration));

                _self.Generation = (byte)(selfClaimedGeneration + 1);
                _logger.LogInformation("Gossip.Mesh received a claim about self, state:{state} generation:{generation}. Raising generation to {generation}", selfClaimedState, selfClaimedGeneration, _self.Generation);

                PushToNodeListeners(new NodeEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));
            }

            // handler sender and everyone else
            while (nodeEvent != null)
            {
                lock (_locker)
                {
                    if (_nodes.TryGetValue(nodeEvent.GossipEndPoint, out var node) &&
                        (node.IsLaterGeneration(nodeEvent.Generation) ||
                        (node.Generation == nodeEvent.Generation && node.State < nodeEvent.State)))
                    {
                        // stops state escalation
                        if (nodeEvent.State == NodeState.Alive && nodeEvent.Generation > node.Generation)
                        {
                            RemoveAwaitingAck(nodeEvent.GossipEndPoint);
                        }

                        node.Update(nodeEvent);
                        _logger.LogInformation("Gossip.Mesh node state changed {node}", node);

                        PushToNodeListeners(nodeEvent);
                    }

                    else if (node == null)
                    {
                        node = new Node(nodeEvent);
                        _nodes.Add(node.GossipEndPoint, node);
                        _logger.LogInformation("Gossip.Mesh node added {node}", node);

                        PushToNodeListeners(nodeEvent);
                    }

                    if (node.State != NodeState.Alive)
                    {
                        AddAwaitingAck(node.GossipEndPoint);
                    }
                }

                nodeEvent = NodeEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream);
            }
        }

        private void WriteNodes(Stream stream, IPEndPoint destinationGossipEndPoint)
        {
            lock (_locker)
            {
                stream.WriteByte(_self.Generation);
                stream.WriteByte(_self.Service);
                stream.WritePort(_self.ServicePort);

                if (_nodes.TryGetValue(destinationGossipEndPoint, out var destinationNode))
                {
                    stream.WriteByte((byte)destinationNode.State);
                    stream.WriteByte(destinationNode.Generation);
                }

                else
                {
                    stream.WriteByte((byte)NodeState.Alive);
                    stream.WriteByte(0x01);
                }

                var nodes = GetNodes(destinationGossipEndPoint);

                if (nodes != null)
                {
                    var i = 0;
                    {
                        while (i < nodes.Length && stream.Position < _options.MaxUdpPacketBytes - 11)
                        {
                            var node = nodes[i];
                            nodes[i].WriteTo(stream);
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

        private Node[] GetNodes(IPEndPoint destinationGossipEndPoint)
        {
            lock (_locker)
            {
                return _nodes
                    .Values
                    .OrderBy(m => m.GossipCounter)
                    .Where(m => !(_awaitingAcks.TryGetValue(m.GossipEndPoint, out var t) && DateTime.UtcNow > t.AddMilliseconds(_options.DeadCoolOffMilliseconds)) &&
                                !EndPointsMatch(destinationGossipEndPoint, m.GossipEndPoint))
                    .ToArray();
            }
        }

        private bool IsNodesEmpty()
        {
            lock (_locker)
            {
                return _nodes.Count == 0;
            }
        }

        private IEnumerable<IPEndPoint> GetRandomGossipEndPoints(int numberOfEndPoints, IPEndPoint directGossipEndPoint = null)
        {
            var nodes = GetNodes(directGossipEndPoint);
            var randomIndex = _rand.Next(0, nodes.Length);

            if (nodes.Length == 0)
            {
                return Enumerable.Empty<IPEndPoint>();
            }

            return Enumerable.Range(randomIndex, numberOfEndPoints)
                .Select(ri => ri % nodes.Length) // wrap the range around to 0 if we hit the end
                .Select(i => nodes[i])
                .Select(m => m.GossipEndPoint)
                .Distinct()
                .Take(numberOfEndPoints);
        }

        private void UpdateNodeState(IPEndPoint gossipEndPoint, NodeState nodeState)
        {
            lock (_locker)
            {
                if (_nodes.TryGetValue(gossipEndPoint, out var node) && node.State < nodeState)
                {
                    node.Update(nodeState);
                    _logger.LogInformation("Gossip.Mesh {nodeState} node {node}", nodeState.ToString().ToLower(), node);

                    var nodeEvent = new NodeEvent(_self.GossipEndPoint, DateTime.UtcNow, node);
                    PushToNodeListeners(nodeEvent);
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

        private void PushToNodeListeners(NodeEvent nodeEvent)
        {
            foreach (var listener in _options.Listeners)
            {
                listener.Accept(nodeEvent).ConfigureAwait(false);
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