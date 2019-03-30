using GossipMesh.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace GossipMesh.Seed.Stores
{
    public class NodeEventsStore : INodeEventsStore
    {
        private readonly object _nodeEventsLocker = new Object();
        private readonly List<NodeEvent> _nodeEvents = new List<NodeEvent>();

        public void Add(NodeEvent nodeEvent)
        {
            _nodeEvents.Add(nodeEvent);
        }

        public NodeEvent[] GetAll()
        {
            lock (_nodeEventsLocker)
            {
                return _nodeEvents.ToArray();
            }
        }
    }
}