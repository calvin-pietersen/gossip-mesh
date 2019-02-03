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
    public class MembersListener : IStateListener
    {
        private readonly IHubContext<MembersHub> _membersHubContext;

        public MembersListener(IHubContext<MembersHub> membersHubContext)
        {
            _membersHubContext = membersHubContext;
        }
        public void MemberStateUpdated(Member member)
        {
            _membersHubContext.Clients.All.SendAsync("MemberStateUpdatedMessage", member).ConfigureAwait(false);
        }
    }
}