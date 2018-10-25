namespace GossipMesh.Core
{
    public enum MessageType : byte
    {
        Ping = 0x01,
        Ack = 0x02,
        PingRequest = 0x03,
        AckRequest = 0x04
    }
}