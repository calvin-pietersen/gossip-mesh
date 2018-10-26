namespace GossipMesh.Core
{
    public enum MemberState : byte
    {
        Alive = 0x01,
        Left = 0x02,
        Dead = 0x03,
        Suspected = 0x04
    }
}