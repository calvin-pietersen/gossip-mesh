using GossipMesh.Core;
using System;
using System.Collections.Generic;
using System.Net;

namespace GossipMesh.Seed.Stores
{
    public interface INodeEventsStore
    {
        void Add(NodeEvent nodeEvent);

        NodeEvent[]  GetAll();
    }
}