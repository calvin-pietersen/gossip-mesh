using System.Net;
using GossipMesh.LoadBalancing;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace GreeterClient
{
    internal class GreeterServiceClientFactory : IServiceClientFactory
    {
        public IServiceClient CreateServiceClient(IPEndPoint serviceEndPoint)
        {        
            Channel channel = new Channel(serviceEndPoint.Address.ToString(), serviceEndPoint.Port, ChannelCredentials.Insecure);
            channel.ConnectAsync().ConfigureAwait(false);

            var client =  new Helloworld.Greeter.GreeterClient(channel);

            return new GreeterServiceClient
            {
                ServiceEndPoint = serviceEndPoint,
                Client = client
            };
        }
    }
}