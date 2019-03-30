namespace GossipMesh.Core
{
    public enum NodeState : byte
    {
        Alive = 0x00,
        Suspicious = 0x01,
        Dead = 0x02,
        Left = 0x03,
        Pruned = 0x04
    }
}