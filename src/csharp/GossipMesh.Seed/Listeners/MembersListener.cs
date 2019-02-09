using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;
using Microsoft.AspNetCore.SignalR;
using GossipMesh.Seed.Hubs;
using Microsoft.Extensions.Logging;

namespace GossipMesh.Seed.Listeners
{
    public class MembersListener : IMemberEventListener
    {
        private readonly object _memberEventsLocker = new Object();
        private readonly Dictionary<IPEndPoint, Dictionary<IPEndPoint, List<MemberEvent>>> _memberEvents = new Dictionary<IPEndPoint, Dictionary<IPEndPoint, List<MemberEvent>>>();
        private readonly IHubContext<MembersHub> _membersHubContext;
        private readonly ILogger _logger;

        public MembersListener(IHubContext<MembersHub> membersHubContext, ILogger<Startup> logger)
        {
            _membersHubContext = membersHubContext;
            _logger = logger;
        }
        public void MemberEventCallback(IPEndPoint senderGossipEndPoint, MemberEvent memberEvent)
        {
            lock (_memberEventsLocker)
            {
                if (_memberEvents.TryGetValue(senderGossipEndPoint, out var senderMemberEvents) &&
                    senderMemberEvents.TryGetValue(memberEvent.GossipEndPoint, out var memberEvents))
                {
                    if (memberEvents.Last().NotEqual(memberEvent))
                    {
                        memberEvents.Add(memberEvent);
                        _membersHubContext.Clients.All.SendAsync("MemberStateUpdatedMessage", senderGossipEndPoint.ToString(), memberEvent.ToString()).ConfigureAwait(false);
                    }
                }
                else if (senderMemberEvents == null)
                {
                    _memberEvents.Add(senderGossipEndPoint, new Dictionary<IPEndPoint, List<MemberEvent>> 
                    { 
                        { memberEvent.GossipEndPoint, new List<MemberEvent> { memberEvent} }
                    });
                    _membersHubContext.Clients.All.SendAsync("MemberStateUpdatedMessage", senderGossipEndPoint.ToString(), memberEvent.ToString()).ConfigureAwait(false);
                }
                else
                {
                    senderMemberEvents.Add(memberEvent.GossipEndPoint, new List<MemberEvent> { memberEvent });
                    _membersHubContext.Clients.All.SendAsync("MemberStateUpdatedMessage", senderGossipEndPoint.ToString(), memberEvent.ToString()).ConfigureAwait(false);
                }
            }
        }
    }
}