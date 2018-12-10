using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;

namespace GossipMesh.Core
{
    public class LoadBalancer : IStateListener
    {
        List<Member> services = new List<Member>();
        Random random = new Random();

        public void NodeStateUpdated(Member member) {
            lock (services) {
                services.RemoveAll(oldMember =>
                    oldMember.IP == member.IP && oldMember.GossipPort == member.GossipPort);
                if (member.State == MemberState.Alive || member.State == MemberState.Suspicious) {
                    services.Add(member);
                }
            }
        }

        public IPEndPoint GetEndpoint(byte serviceType) {
            lock (services) {
                var eligible = services.Where(Member => Member.Service == serviceType).ToList();
                if (eligible.Count > 0) {
                    int index = random.Next(eligible.Count);
                    return eligible[index].ServiceEndPoint;
                } else {
                    throw new Exception("Ahhhhh! No candidates for service type");
                }
            }
        }
    }
}