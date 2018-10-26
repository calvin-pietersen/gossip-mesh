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

        public IPEndPoint GossipEndPoint
        {
            get
            {
                return new IPEndPoint(IP, GossipPort);
            }
        }

        public void WriteTo(Stream stream)
        {
            stream.WriteByte((byte)State);

            if (State == MemberState.Alive)
            {
                stream.Write(IP.GetAddressBytes(), 0, 4);

                stream.WriteByte((byte)GossipPort);
                stream.WriteByte((byte)(GossipPort >> 8));

                stream.WriteByte((byte)ServicePort);
                stream.WriteByte((byte)(ServicePort >> 8));
                stream.WriteByte((byte)ServiceId);
                stream.WriteByte((byte)Generation);
            }
        }

        public override string ToString() 
        {
            return string.Format("ip {0} gossipPort {1} servicePort {2} serviceId {3} generation {4} ipEndPoint {5} state {6}",
            IP,
            GossipPort,
            ServicePort,
            ServiceId,
            Generation,
            GossipEndPoint,
            State);
        }
    }
}