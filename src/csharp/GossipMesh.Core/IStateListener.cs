namespace GossipMesh.Core
{
    public interface IStateListener
    {
        void NodeStateUpdated(Member state);
    }
}