using System.Net;

namespace GossipMesh.LoadBalancing
{
    public interface IServiceClientFactory
    {
        IServiceClient CreateServiceClient(IPEndPoint serviceEndPoint);
    }
}