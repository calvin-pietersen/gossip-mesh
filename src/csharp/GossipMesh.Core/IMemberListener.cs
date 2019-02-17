using System.Net;

namespace GossipMesh.Core
{
    public interface IMemberListener
    {
        void MemberCallback(Member Member);
    }
}