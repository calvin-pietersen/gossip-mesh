using System.IO;
using System.Net;

namespace GossipMesh.Core
{
    public class Member
    {
        public MemberState State { get; set; }
        public IPAddress IP { get; set; }
        public ushort GossipPort { get; set; }
        public byte Generation { get; set; }
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
            stream.WriteIPEndPoint(GossipEndPoint);
            stream.WriteByte(Generation);

            if (State == MemberState.Alive)
            {
                stream.WritePort(ServicePort);
                stream.WriteByte((byte)Service);
            }
        }

        public override string ToString()
        {
            return string.Format("State:{0} IP:{1} GossipPort:{2} Generation:{3} Service:{4} ServicePort:{5}",
            State,
            IP,
            GossipPort,
            Generation,
            Service,
            ServicePort);
        }
    }
}