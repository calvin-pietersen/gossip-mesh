using GossipMesh.Core;
using System;
using System.Collections.Generic;
using System.Net;

namespace GossipMesh.SeedUI.Stores
{
    public interface IMemberGraphStore
    {
        Graph.Node AddOrUpdateNode(MemberEvent memberEvent);

        Graph GetGraph();
    }
}