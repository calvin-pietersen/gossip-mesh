using GossipMesh.Core;
using System;
using System.Collections.Generic;
using System.Net;

namespace GossipMesh.Seed.Stores
{
    public interface IMemberGraphStore
    {
        Graph.Node AddOrUpdateNode(MemberEvent memberEvent);

        Graph GetGraph();
    }
}