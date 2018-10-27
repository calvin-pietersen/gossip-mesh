using System.IO;
using System.Net;

namespace GossipMesh.Core
{
    public class Member
    {
        private IPAddress _ip;
        private byte[] _ipBytes;

        public MemberState State { get; set; }
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
        public ushort Generation { get; set; }
        public ushort Service { get; set; }
        public ushort ServicePort { get; set; }

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

            stream.Write(_ipBytes, 0, 4);
            stream.WriteByte((byte)GossipPort);
            stream.WriteByte((byte)(GossipPort >> 8));
            stream.WriteByte((byte)Generation);

            if (State == MemberState.Alive)
            {
                stream.WriteByte((byte)ServicePort);
                stream.WriteByte((byte)(ServicePort >> 8));
                stream.WriteByte((byte)Service);
            }
        }

        public override string ToString()
        {
            return string.Format("State {0} IP {1} GossipPort {2} Generation {3} Service {4} ServicePort {5}",
            IP,
            GossipPort,
            Generation,
            ServicePort,
            Service,
            State);
        }
    }
}