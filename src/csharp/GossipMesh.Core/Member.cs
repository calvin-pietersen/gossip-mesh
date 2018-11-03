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
        public byte Service { get; set; }
        public ushort ServicePort { get; set; }

        public IPEndPoint GossipEndPoint
        {
            get
            {
                return new IPEndPoint(IP, GossipPort);
            }
        }

        public bool IsLaterGeneration(byte newGeneration)
        {
            return ((0 < (newGeneration - Generation)) && ((newGeneration - Generation) < 191))
                 || ((newGeneration - Generation) <= -191);
        }

        public bool IsStateSuperseded(MemberState newState)
        {
            // alive < suspicious < dead < left
            return (State == MemberState.Alive && newState != MemberState.Alive) ||
                (State == MemberState.Suspicious && (newState == MemberState.Dead || newState == MemberState.Left)) ||
                State == MemberState.Dead && newState == MemberState.Left;
        }


        public void WriteTo(Stream stream)
        {
            stream.WriteByte((byte)State);
            stream.WriteIPEndPoint(GossipEndPoint);
            stream.WriteByte(Generation);

            if (State == MemberState.Alive)
            {
                stream.WritePort(ServicePort);
                stream.WriteByte(Service);
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