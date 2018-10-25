using System.IO;
using System.Net;

namespace GossipMesh.Core
{
    public class Member
    {
        private IPAddress _ip;
        private byte[] _ipBytes;
        public IPAddress IP
        {
            get
            {
                return _ip;
            }
            set
            {
                _ip = value;
                _ipBytes = value.GetAddressBytes();
            }
        }

        public ushort GossipPort { get; set; }
        public ushort ServicePort { get; set; }
        public ushort ServiceId { get; set; }
        public ushort Generation { get; set; }
        public MemberState State { get; set; }

        public IPEndPoint GossipEndpoint
        {
            get
            {
                return new IPEndPoint(IP, GossipPort);
            }
        }

        public void WriteTo(Stream stream)
        {
            if (State == MemberState.Alive)
            {
                stream.WriteByte(0x01);

                var ipBytes = IP.GetAddressBytes();
                stream.WriteByte(ipBytes[0]);
                stream.WriteByte(ipBytes[1]);
                stream.WriteByte(ipBytes[2]);
                stream.WriteByte(ipBytes[3]);

                stream.WriteByte((byte)GossipPort);
                stream.WriteByte((byte)(GossipPort >> 8));

                stream.WriteByte((byte)ServicePort);
                stream.WriteByte((byte)(ServicePort >> 8));
                stream.WriteByte((byte)ServiceId);
                stream.WriteByte((byte)Generation);
            }
        }
    }
}