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
    public class MemberEventsListener : IMemberEventsListener
    {
        private readonly IMemberEventsStore _memberEventsStore;
        private readonly IMemberGraphStore _memberGraphStore;
        private readonly IHubContext<MembersHub> _membersHubContext;
        private readonly ILogger _logger;

        public MemberEventsListener(IMemberGraphStore memberGraphStore, IMemberEventsStore memberEventsStore, IHubContext<MembersHub> membersHubContext, ILogger<Startup> logger)
        {
            _memberGraphStore = memberGraphStore;
            _memberEventsStore = memberEventsStore;
            _membersHubContext = membersHubContext;
            _logger = logger;
        }

        public async Task MemberEventsCallback(IEnumerable<MemberEvent> memberEvents)
        {
            try
            {
                var newMemberEvents = memberEvents.Where(m => _memberEventsStore.Add(m)).ToArray();
                if (newMemberEvents.Any())
                {
                    await _membersHubContext.Clients.All.SendAsync("MemberEventsMessage", newMemberEvents).ConfigureAwait(false);
                }

                if (_memberGraphStore.Update(memberEvents))
                {
                    await _membersHubContext.Clients.All.SendAsync("MemberGraphUpdatedMessage", _memberGraphStore.GetGraph()).ConfigureAwait(false);
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
            }
        }
    }
}