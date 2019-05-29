using GossipMesh.Core;
using GossipMesh.SeedUI.Stores;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace GossipMesh.SeedUI.Hubs
{
    public class MembersHub : Hub
    {
        private readonly IMemberEventsStore _memberEventsStore;
        private readonly IMemberGraphStore _memberGraphStore;

        private readonly ILogger _logger;

        public MembersHub(IMemberGraphStore memberGraphStore, IMemberEventsStore memberEventsStore, ILogger<Startup> logger)
        {
            _memberGraphStore = memberGraphStore;
            _memberEventsStore = memberEventsStore;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("InitializationMessage", _memberGraphStore.GetGraph(), _memberEventsStore.GetAll()).ConfigureAwait(false);
        }
    }
}