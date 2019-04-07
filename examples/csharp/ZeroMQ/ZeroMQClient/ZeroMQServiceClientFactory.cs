using System;
using System.Net;
using GossipMesh.LoadBalancing;
using NetMQ.Sockets;

namespace ZeroMQClient
{
    internal class ZeroMQServiceClientFactory : IServiceClientFactory
    {
        public IServiceClient CreateServiceClient(IPEndPoint serviceEndPoint)
        {
            var client = new RequestSocket();
            client.Options.Linger = TimeSpan.Zero;
            client.Connect("tcp://" + serviceEndPoint.ToString());

            return new ZeroMQServiceClient
            {
                ServiceEndPoint = serviceEndPoint,
                Client = client
            };
        }
    }
}