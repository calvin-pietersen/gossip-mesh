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
    public class MemberEventsStore : IMemberEventsStore
    {
        private readonly object _memberEventsLocker = new Object();
        private readonly Dictionary<IPEndPoint, Dictionary<IPEndPoint, List<MemberEvent>>> _memberEvents = new Dictionary<IPEndPoint, Dictionary<IPEndPoint, List<MemberEvent>>>();

        public bool Add(MemberEvent memberEvent)
        {
            var wasAdded = true;

            lock (_memberEventsLocker)
            {
                if (_memberEvents.TryGetValue(memberEvent.SenderGossipEndPoint, out var senderMemberEvents) &&
                    senderMemberEvents.TryGetValue(memberEvent.GossipEndPoint, out var memberEvents))
                {
                    if (memberEvents.Last().NotEqual(memberEvent))
                    {
                        memberEvents.Add(memberEvent);
                    }

                    else
                    {
                        wasAdded = false;
                    }
                }

                else if (senderMemberEvents == null)
                {
                    _memberEvents.Add(memberEvent.SenderGossipEndPoint, new Dictionary<IPEndPoint, List<MemberEvent>>
                    {
                        { memberEvent.GossipEndPoint, new List<MemberEvent> { memberEvent} }
                    });
                }

                else
                {
                    senderMemberEvents.Add(memberEvent.GossipEndPoint, new List<MemberEvent> { memberEvent });
                }

                return wasAdded;
            }
        }

        public MemberEvent[] GetAll()
        {
            lock (_memberEventsLocker)
            {
                return _memberEvents
                        .SelectMany(senderMemberEvents => senderMemberEvents.Value
                            .SelectMany(memberEvents => memberEvents.Value)).ToArray();
            }
        }

        public Graph GetGraph()
        {
            lock (_memberEventsLocker)
            {
                var nodes = _memberEvents
                                .Select(member => _memberEvents.Values
                                                    .Select(senderMemberEvents => senderMemberEvents.GetValueOrDefault(member.Key, null)?.Last())
                                                    .Where(m => m != null)
                                                    .OrderBy(m => m.Generation)
                                                    .ThenBy(m => m.State)
                                                    .Last())
                                .Select(latestMemberEvent => new Graph.Node
                                {
                                    Id = latestMemberEvent.GossipEndPoint,
                                    Ip = latestMemberEvent.IP,
                                    State = latestMemberEvent.State,
                                    Generation = latestMemberEvent.Generation,
                                    Service = latestMemberEvent.Service,
                                    ServicePort = latestMemberEvent.ServicePort
                                }).ToArray();

                var links = _memberEvents
                                .SelectMany(senderMemberEvents => senderMemberEvents.Value
                                    .Where(memberEvents => !senderMemberEvents.Key.Equals(memberEvents.Key))
                                    .Select(memberEvents =>
                                        new Graph.Link { Source = senderMemberEvents.Key, Target = memberEvents.Key }))
                                .Distinct()
                                .ToArray();
                return new Graph
                {
                    Nodes = nodes,
                    Links = links
                };
            }
        }
    }
}