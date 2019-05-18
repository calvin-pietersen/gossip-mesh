using System.Net;
using GossipMesh.LoadBalancing;
using NetMQ.Sockets;

namespace ZeroMQClient
{
    internal class ZeroMQServiceClient : IServiceClient
    {
        public IPEndPoint ServiceEndPoint { get; set; }
        public RequestSocket Client { get; set; }
    }
}