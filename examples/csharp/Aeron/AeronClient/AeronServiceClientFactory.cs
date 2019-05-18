using System.Net;
using Adaptive.Aeron;
using GossipMesh.LoadBalancing;

namespace AeronClient
{
    internal class AeronServiceClientFactory : IServiceClientFactory
    {
        public IServiceClient CreateServiceClient(IPEndPoint serviceEndPoint)
        {
            string channel = "aeron:udp?endpoint=" + serviceEndPoint.ToString();
            var ctx = new Aeron.Context();
            var aeron = Aeron.Connect(ctx);
            var publication = aeron.AddPublication(channel, 10);

            return new AeronServiceClient
            {
                ServiceEndPoint = serviceEndPoint,
                Publication = publication
            };
        }
    }
}