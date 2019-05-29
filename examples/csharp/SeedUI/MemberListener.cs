using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;
using Microsoft.AspNetCore.SignalR;
using GossipMesh.SeedUI.Hubs;
using Microsoft.Extensions.Logging;
using GossipMesh.SeedUI.Stores;
using System.Threading.Tasks;

namespace GossipMesh.SeedUI
{
    public class MemberListener : IMemberListener
    {
        private readonly IMemberGraphStore _memberGraphStore;
        private readonly IMemberEventsStore _memberEventsStore;
        private readonly IHubContext<MembersHub> _membersHubContext;
        private readonly ILogger _logger;

        public MemberListener(IMemberGraphStore memberGraphStore, IMemberEventsStore memberEventsStore, IHubContext<MembersHub> membersHubContext, ILogger<Startup> logger)
        {
            _memberGraphStore = memberGraphStore;
            _memberEventsStore = memberEventsStore;
            _membersHubContext = membersHubContext;
            _logger = logger;
        }

        public async Task MemberUpdatedCallback(MemberEvent memberEvent)
        {
            _memberEventsStore.Add(memberEvent);
            var node = _memberGraphStore.AddOrUpdateNode(memberEvent);
            await _membersHubContext.Clients.All.SendAsync("MemberUpdatedMessage", memberEvent, node).ConfigureAwait(false);
        }
    }
}