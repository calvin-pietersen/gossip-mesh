using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;

namespace GossipMesh.LoadBalancing
{
    public class RandomLoadBalancer : ILoadBalancer, IStateListener
    {
        List<Member> services = new List<Member>();
        Random random = new Random();

        public void MemberStateUpdated(Member member) {
            
        }

        public IPEndPoint GetEndpoint(byte serviceType) {
            return null;
        }
    }
}