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
    public class NodeListener : IListener
    {
        private readonly INodeGraphStore _nodeGraphStore;
        private readonly INodeEventsStore _nodeEventsStore;
        private readonly IHubContext<NodesHub> _nodesHubContext;
        private readonly ILogger _logger;

        public NodeListener(INodeGraphStore nodeGraphStore, INodeEventsStore nodeEventsStore, IHubContext<NodesHub> nodesHubContext, ILogger<Startup> logger)
        {
            _nodeGraphStore = nodeGraphStore;
            _nodeEventsStore = nodeEventsStore;
            _nodesHubContext = nodesHubContext;
            _logger = logger;
        }

        public async Task Accept(NodeEvent nodeEvent)
        {
            _nodeEventsStore.Add(nodeEvent);
            var node = _nodeGraphStore.AddOrUpdateNode(nodeEvent);
            await _nodesHubContext.Clients.All.SendAsync("NodeUpdatedMessage", nodeEvent, node).ConfigureAwait(false);
        }
    }
}