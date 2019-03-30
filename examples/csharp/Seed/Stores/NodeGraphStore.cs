using GossipMesh.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace GossipMesh.Seed.Stores
{
    public class NodeGraphStore : INodeGraphStore
    {
        private readonly object _nodeGraphLocker = new Object();
        private readonly Dictionary<IPEndPoint, Graph.Node> _nodes = new Dictionary<IPEndPoint, Graph.Node>();
        private readonly Random _random = new Random();

        public Graph.Node AddOrUpdateNode(NodeEvent nodeEvent)
        {
            Graph.Node node;
            lock (_nodeGraphLocker)
            {
                if (!_nodes.TryGetValue(nodeEvent.GossipEndPoint, out node))
                {
                    node = new Graph.Node
                    {
                        Id = nodeEvent.GossipEndPoint,
                        Ip = nodeEvent.IP,
                        State = nodeEvent.State,
                        Generation = nodeEvent.Generation,
                        Service = nodeEvent.Service,
                        ServicePort = nodeEvent.ServicePort,
                        X = (byte)_random.Next(0, 255),
                        Y = (byte)_random.Next(0, 255)
                    };

                    _nodes.Add(nodeEvent.GossipEndPoint, node);
                }

                else if (nodeEvent.State == NodeState.Alive)
                {
                    node = new Graph.Node
                    {
                        Id = nodeEvent.GossipEndPoint,
                        Ip = nodeEvent.IP,
                        State = nodeEvent.State,
                        Generation = nodeEvent.Generation,
                        Service = nodeEvent.Service,
                        ServicePort = nodeEvent.ServicePort,
                        X = node.X,
                        Y = node.Y
                    };

                    _nodes[nodeEvent.GossipEndPoint] = node;
                }

                else
                {
                    node = new Graph.Node
                    {
                        Id = nodeEvent.GossipEndPoint,
                        Ip = nodeEvent.IP,
                        State = nodeEvent.State,
                        Generation = nodeEvent.Generation,
                        Service = node.Service,
                        ServicePort = node.ServicePort,
                        X = node.X,
                        Y = node.Y
                    };

                    if (nodeEvent.State == NodeState.Pruned)
                    {
                        _nodes.Remove(nodeEvent.GossipEndPoint);
                    }

                    else
                    {
                        _nodes[nodeEvent.GossipEndPoint] = node;
                    }
                }

                return node;
            }
        }

        public Graph GetGraph()
        {
            lock (_nodeGraphLocker)
            {
                return new Graph
                {
                    Nodes = _nodes.Values.ToArray()
                };
            }
        }
    }
}