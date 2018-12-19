namespace GossipMesh.Core
{
    public interface IStateListener
    {
        void MemberStateUpdated(Member state);
    }
}