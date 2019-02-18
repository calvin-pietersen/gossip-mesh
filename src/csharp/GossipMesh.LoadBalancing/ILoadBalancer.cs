using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;

namespace GossipMesh.LoadBalancing
{
    public interface ILoadBalancer
    {
        T GetServiceClient<T>(byte serviceType) where T : IServiceClient;
    }
}