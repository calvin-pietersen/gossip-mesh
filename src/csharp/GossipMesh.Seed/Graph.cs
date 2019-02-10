using System.Net;

namespace GossipMesh.Seed
{
    public class Graph
    {
        public Node[] Nodes { get; set; }
        public Link[] Links { get; set; }

        public class Node
        {
            public IPEndPoint Id { get; set; }
            public IPEndPoint Ip { get; set; }
        }

        public class Link
        {
            public IPEndPoint Source { get; set; }
            public IPEndPoint Target { get; set; }
        }
    }
}