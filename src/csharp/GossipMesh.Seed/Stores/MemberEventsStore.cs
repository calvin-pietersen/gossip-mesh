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
            if (memberEvent.SenderGossipEndPoint.Address == memberEvent.GossipEndPoint.Address && memberEvent.SenderGossipEndPoint.Port == memberEvent.GossipEndPoint.Port)
            {
                return false;
            }

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
    }
}