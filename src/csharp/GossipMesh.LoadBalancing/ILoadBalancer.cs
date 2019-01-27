using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;

namespace GossipMesh.LoadBalancing
{
    public interface ILoadBalancer
    {
        IPEndPoint GetEndpoint(byte serviceType);
    }
}