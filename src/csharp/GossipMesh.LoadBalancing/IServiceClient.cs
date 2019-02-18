using System.Net;

namespace GossipMesh.LoadBalancing
{
    public interface IServiceClient
    {
        IPEndPoint ServiceEndPoint { get; set; }
    }
}