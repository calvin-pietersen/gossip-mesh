using System.Net;
using GossipMesh.LoadBalancing;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace GreeterClient
{
    internal class GreeterClientFactory : IServiceClientFactory
    {
        public object CreateServiceClient(IPEndPoint serviceEndPoint)
        {
            Channel channel = new Channel(serviceEndPoint.Address.ToString(), serviceEndPoint.Port, ChannelCredentials.Insecure);
            return new Helloworld.Greeter.GreeterClient(channel);
        }
    }
}