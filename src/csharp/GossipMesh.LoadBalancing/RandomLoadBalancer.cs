using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;

namespace GossipMesh.LoadBalancing
{
    public class RandomLoadBalancer : ILoadBalancer, IMemberEventListener
    {
        List<Member> services = new List<Member>();
        Random random = new Random();

        public void MemberEventCallback(IPEndPoint senderGossipEndPoint, MemberEvent memberEvent) {
            
        }

        public IPEndPoint GetEndpoint(byte serviceType) {
            return null;
        }
    }
}