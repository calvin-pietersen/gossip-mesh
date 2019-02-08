using System.IO;
using System.Net;
using System.Threading;

namespace GossipMesh.Core
{
    public class MemberEvent
    {
        public MemberState State { get; private set; }
        public IPAddress IP { get; private set; }
        public ushort GossipPort { get; private set; }
        public byte Generation { get; private set; }
        public byte Service { get; private set; }
        public ushort ServicePort { get; private set; }

        internal IPEndPoint GossipEndPoint 
        {
            get { return new IPEndPoint(IP, GossipPort); }
        }

        private MemberEvent()
        {
        }

        internal MemberEvent(Member member)
        {
            State = member.State;
            IP = member.IP;
            GossipPort = member.GossipPort;
            Generation = member.Generation;
            Service = member.Service;
            ServicePort = member.ServicePort;
        }

        internal static MemberEvent ReadFrom(Stream stream)
        {
            var memberEvent = new MemberEvent
            {
                State = stream.ReadMemberState(),
                IP = stream.ReadIPAddress(),
                GossipPort = stream.ReadPort(),
                Generation = (byte)stream.ReadByte(),
            };

            if (memberEvent.State == MemberState.Alive)
            {
                memberEvent.Service = (byte)stream.ReadByte();
                memberEvent.ServicePort = stream.ReadPort();
            }

            return memberEvent;
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