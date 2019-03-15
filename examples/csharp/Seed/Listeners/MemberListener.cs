using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;
using Microsoft.AspNetCore.SignalR;
using GossipMesh.Seed.Hubs;
using Microsoft.Extensions.Logging;
using GossipMesh.Seed.Stores;
using System.Threading.Tasks;

namespace GossipMesh.Seed.Listeners
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
            if (_memberGraphStore.TryAddOrUpdateNode(memberEvent, out var node))
            {
                await _membersHubContext.Clients.All.SendAsync("NodeUpdatedMessage", node).ConfigureAwait(false);
            }

            if (_memberEventsStore.Add(memberEvent))
            {
                await _membersHubContext.Clients.All.SendAsync("MemberEventMessage", memberEvent).ConfigureAwait(false);
            }
        }
    }
}