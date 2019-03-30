using GossipMesh.Core;
using System;
using System.Collections.Generic;
using System.Net;

namespace GossipMesh.Seed.Stores
{
    public interface INodeGraphStore
    {
        Graph.Node AddOrUpdateNode(NodeEvent nodeEvent);

        Graph GetGraph();
    }
}