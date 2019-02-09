using System.Net;

namespace GossipMesh.Core
{
    public interface IMemberEventListener
    {
        void MemberEventCallback(IPEndPoint senderGossipEndPoint, MemberEvent MemberEvent);
    }
}