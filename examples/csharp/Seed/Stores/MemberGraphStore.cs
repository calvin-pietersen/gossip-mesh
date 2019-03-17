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
    public class MemberGraphStore : IMemberGraphStore
    {
        private readonly object _memberGraphLocker = new Object();
        private readonly Dictionary<IPEndPoint, Graph.Node> _nodes = new Dictionary<IPEndPoint, Graph.Node>();
        private readonly Random _random = new Random();

        public Graph.Node AddOrUpdateNode(MemberEvent memberEvent)
        {
            Graph.Node node;
            lock (_memberGraphLocker)
            {
                if (!_nodes.TryGetValue(memberEvent.GossipEndPoint, out node))
                {
                    node = new Graph.Node
                    {
                        Id = memberEvent.GossipEndPoint,
                        Ip = memberEvent.IP,
                        State = memberEvent.State,
                        Generation = memberEvent.Generation,
                        Service = memberEvent.Service,
                        ServicePort = memberEvent.ServicePort,
                        X = (byte)_random.Next(0, 255),
                        Y = (byte)_random.Next(0, 255)
                    };

                    _nodes.Add(memberEvent.GossipEndPoint, node);
                }

                else if (memberEvent.State == MemberState.Alive)
                {
                    node = new Graph.Node
                    {
                        Id = memberEvent.GossipEndPoint,
                        Ip = memberEvent.IP,
                        State = memberEvent.State,
                        Generation = memberEvent.Generation,
                        Service = memberEvent.Service,
                        ServicePort = memberEvent.ServicePort,
                        X = node.X,
                        Y = node.Y
                    };

                    _nodes[memberEvent.GossipEndPoint] = node;
                }

                else
                {
                    node = new Graph.Node
                    {
                        Id = memberEvent.GossipEndPoint,
                        Ip = memberEvent.IP,
                        State = memberEvent.State,
                        Generation = memberEvent.Generation,
                        Service = node.Service,
                        ServicePort = node.ServicePort,
                        X = node.X,
                        Y = node.Y
                    };

                    if (memberEvent.State == MemberState.Pruned)
                    {
                        _nodes.Remove(memberEvent.GossipEndPoint);
                    }

                    else
                    {
                        _nodes[memberEvent.GossipEndPoint] = node;
                    }
                }

                return node;
            }
        }

        public Graph GetGraph()
        {
            lock (_memberGraphLocker)
            {
                return new Graph
                {
                    Nodes = _nodes.Values.ToArray()
                };
            }
        }
    }
}