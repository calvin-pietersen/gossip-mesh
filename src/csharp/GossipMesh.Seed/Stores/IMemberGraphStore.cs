using GossipMesh.Core;
using System;
using System.Collections.Generic;
using System.Net;

namespace GossipMesh.Seed.Stores
{
    public interface IMemberGraphStore
    {
        bool Update(IEnumerable<MemberEvent> memberEvents);

        Graph GetGraph();
    }
}