using System.Net;
using GossipMesh.LoadBalancing;
using Grpc.Core;
using Helloworld;

namespace GossipMesh.Example
{
    internal class GreeterClientFactory : IServiceClientFactory
    {
        public object CreateServiceClient(IPEndPoint serviceEndPoint)
        {
            Channel channel = new Channel(serviceEndPoint.Address.ToString(), serviceEndPoint.Port, ChannelCredentials.Insecure);
            return new Greeter.GreeterClient(channel);
        }
    }
}