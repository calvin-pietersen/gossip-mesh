using GossipMesh.Core;
using GossipMesh.Seed.Stores;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace GossipMesh.Seed.Hubs
{
    public class MembersHub : Hub
    {
        private readonly IMemberEventsStore _memberEventsStore;

        private readonly ILogger _logger;

        public MembersHub(IMemberEventsStore memberEventsStore, ILogger<Startup> logger)
        {
            _memberEventsStore = memberEventsStore;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("InitializationMessage", _memberEventsStore.GetGraph(), _memberEventsStore.GetAll()).ConfigureAwait(false);
        }
    }
}