using System.Net;

namespace GossipMesh.LoadBalancing
{
    public interface IServiceClientFactory
    {
        object CreateServiceClient(IPEndPoint serviceEndPoint);
    }
}