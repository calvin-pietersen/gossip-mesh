namespace GossipMesh.Core
{
    public enum MessageType : byte
    {
        Ping = 0x01,
        Ack = 0x00,
        PingRequest = 0x03,
        AckRequest = 0x02,
    }
}