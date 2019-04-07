using System.Net;
using Adaptive.Aeron;
using GossipMesh.LoadBalancing;

namespace AeronClient
{
    internal class AeronServiceClient : IServiceClient
    {
        public IPEndPoint ServiceEndPoint { get; set; }
        public Publication Publication { get; set; }
    }
}