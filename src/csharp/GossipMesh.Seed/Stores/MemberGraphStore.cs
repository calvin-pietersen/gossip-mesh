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
        private readonly Dictionary<string, Graph.Link> _links = new Dictionary<string, Graph.Link>();

        public bool TryAddOrUpdateNode(MemberEvent memberEvent, out Graph.Node node)
        {
            var wasAddedOrUpdated = false;
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
                        ServicePort = memberEvent.ServicePort
                    };

                    _nodes.Add(memberEvent.GossipEndPoint, node);
                    wasAddedOrUpdated = true;
                }

                else if (memberEvent.Generation > node.Generation ||
                    (memberEvent.Generation == node.Generation && memberEvent.State > node.State))
                {
                    node = new Graph.Node
                    {
                        Id = memberEvent.GossipEndPoint,
                        Ip = memberEvent.IP,
                        State = memberEvent.State,
                        Generation = memberEvent.Generation,
                        Service = memberEvent.Service,
                        ServicePort = memberEvent.ServicePort
                    };

                    _nodes[memberEvent.GossipEndPoint] = node;
                    wasAddedOrUpdated = true;
                }
            }

            return wasAddedOrUpdated;
        }

        public Graph GetGraph()
        {
            lock (_memberGraphLocker)
            {
                return new Graph
                {
                    Nodes = _nodes.Values.ToArray(),
                    Links = _links.Values.ToArray()
                };
            }
        }
    }
}