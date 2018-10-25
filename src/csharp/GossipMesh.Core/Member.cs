using System.Net;

namespace GossipMesh.Core
{
    public class Member
    {
        public IPAddress IP { get; set; }
        public ushort GossipPort { get; set; }
        public ushort ServicePort { get; set; }
        public ushort ServiceId { get; set; }
        public ushort Generation { get; set; }
        public MemberState State { get; set; }

        public byte[] GetStatusBytes()
        {
            byte[] bytes;
            if (State == MemberState.Alive)
            {
                bytes = new byte[10];
                bytes[0] = 0x01;
                
                var ipBytes = IP.GetAddressBytes();
                bytes[1] = ipBytes[0];
                bytes[2] = ipBytes[1];
                bytes[3] = ipBytes[2];
                bytes[4] = ipBytes[3];

                bytes[5] = (byte)GossipPort;
                bytes[6] = (byte)(GossipPort >> 8);

                bytes[7] = (byte)ServicePort;
                bytes[7] = (byte)(ServicePort >> 8);
                bytes[8] = (byte)ServiceId;
                bytes[9] = (byte)Generation;
            }
            else
            {
                bytes = new byte[0];
            }

            return bytes;
        }
    }
}