using GossipMesh.Core;
using System;
using System.Collections.Generic;
using System.Net;

namespace GossipMesh.Seed.Stores
{
    public interface IMemberEventsStore
    {
        bool Add(IPEndPoint senderGossipEndPoint, MemberEvent memberEvent);

        Dictionary<IPEndPoint, Dictionary<IPEndPoint, List<MemberEvent>>> GetAll();
    }
}