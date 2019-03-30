using GossipMesh.Core;
using GossipMesh.Seed.Stores;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace GossipMesh.Seed.Hubs
{
    public class NodesHub : Hub
    {
        private readonly INodeEventsStore _nodeEventsStore;
        private readonly INodeGraphStore _nodeGraphStore;

        private readonly ILogger _logger;

        public NodesHub(INodeGraphStore nodeGraphStore, INodeEventsStore nodeEventsStore, ILogger<Startup> logger)
        {
            _nodeGraphStore = nodeGraphStore;
            _nodeEventsStore = nodeEventsStore;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("InitializationMessage", _nodeGraphStore.GetGraph(), _nodeEventsStore.GetAll()).ConfigureAwait(false);
        }
    }
}