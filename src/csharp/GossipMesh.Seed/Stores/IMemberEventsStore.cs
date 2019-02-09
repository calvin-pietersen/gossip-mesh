using GossipMesh.Core;
using System;
using System.Collections.Generic;
using System.Net;

namespace GossipMesh.Seed.Stores
{
    public interface IMemberEventsStore
    {
        bool Add(MemberEvent memberEvent);

        MemberEvent[]  GetAll();
    }
}