using GossipMesh.Core;
using System;
using System.Collections.Generic;
using System.Net;

namespace GossipMesh.Seed.Stores
{
    public interface IMemberGraphStore
    {
        bool TryAddOrUpdateNode(MemberEvent memberEvent, out Graph.Node node);

        Graph GetGraph();
    }
}