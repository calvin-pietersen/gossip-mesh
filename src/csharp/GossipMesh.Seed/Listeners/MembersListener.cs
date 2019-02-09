using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;
using Microsoft.AspNetCore.SignalR;
using GossipMesh.Seed.Hubs;
using Microsoft.Extensions.Logging;
using GossipMesh.Seed.Stores;

namespace GossipMesh.Seed.Listeners
{
    public class MembersListener : IMemberEventListener
    {
        private readonly IMemberEventsStore _memberEventsStore;
        private readonly IHubContext<MembersHub> _membersHubContext;
        private readonly ILogger _logger;

        public MembersListener(IMemberEventsStore memberEventsStore, IHubContext<MembersHub> membersHubContext, ILogger<Startup> logger)
        {
            _memberEventsStore = memberEventsStore;
            _membersHubContext = membersHubContext;
            _logger = logger;
        }

        public void MemberEventCallback(IPEndPoint senderGossipEndPoint, MemberEvent memberEvent)
        {
            if(_memberEventsStore.Add(senderGossipEndPoint, memberEvent))
            {
                _membersHubContext.Clients.All.SendAsync("MemberStateUpdatedMessage", senderGossipEndPoint, memberEvent).ConfigureAwait(false);
            }
        }
    }
}