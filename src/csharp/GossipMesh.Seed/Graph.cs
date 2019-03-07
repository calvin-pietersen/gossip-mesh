using System;
using System.Net;
using GossipMesh.Core;

namespace GossipMesh.Seed
{
    public class Graph
    {
        public Node[] Nodes { get; set; }

        public class Node
        {
            public IPEndPoint Id { get; set; }
            public IPAddress Ip { get; set; }
            public MemberState State { get; set; }
            public byte Generation { get; set; }
            public byte Service { get; set; }
            public ushort ServicePort { get; set; }
        }
    }
}