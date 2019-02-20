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

        public bool Update(IEnumerable<MemberEvent> memberEvents)
        {
            var wasUpdated = false;
            foreach (var memberEvent in memberEvents)
            {
                var serviceIpEndPoint = new IPEndPoint(memberEvent.IP, memberEvent.ServicePort);

                lock (_memberGraphLocker)
                {
                    Graph.Node node;
                    if (!_nodes.TryGetValue(serviceIpEndPoint, out node))
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

                        _nodes.Add(serviceIpEndPoint, node);
                        wasUpdated = true;
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

                        wasUpdated = true;
                    }

                    if (!memberEvent.SenderGossipEndPoint.Address.Equals(memberEvent.GossipEndPoint) && memberEvent.SenderGossipEndPoint.Port != memberEvent.GossipEndPoint.Port)
                    {
                        Graph.Link link;
                        var linkId = Graph.Link.GetLinkId(memberEvent.SenderGossipEndPoint, memberEvent.GossipEndPoint);
                        if (!_links.TryGetValue(linkId, out link))
                        {
                            link = new Graph.Link { Source = memberEvent.SenderGossipEndPoint, Target = memberEvent.GossipEndPoint };
                            _links.Add(linkId, link);

                            wasUpdated = true;
                        }
                    }
                }
            }

            return wasUpdated;
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