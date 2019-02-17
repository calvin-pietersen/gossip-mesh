using System.Net;
using GossipMesh.LoadBalancing;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace GreeterClient
{
    internal class GreeterServiceClient : IServiceClient
    {
        public IPEndPoint ServiceEndPoint { get; set; }
        public Helloworld.Greeter.GreeterClient Client { get; set; }
    }
}