using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;

namespace GossipMesh.LoadBalancing
{
    public interface ILoadBalancer
    {
        void RegisterServiceClientFactory(byte serviceType, IServiceClientFactory serviceClientFactory);
        T GetServiceClient<T>(byte serviceType);
    }
}