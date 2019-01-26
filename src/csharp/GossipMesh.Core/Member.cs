using System.IO;
using System.Net;
using System.Threading;

namespace GossipMesh.Core
{
    public class Member
    {
        private long _gossipCounter = 0;

        public MemberState State { get; set; }
        public IPAddress IP { get; set; }
        public ushort GossipPort { get; set; }
        public byte Generation { get; set; }
        public byte Service { get; set; }
        public ushort ServicePort { get; set; }
        public long GossipCounter { get { return Interlocked.Read(ref _gossipCounter); } }
        public IPEndPoint GossipEndPoint
        {
            get
            {
                return new IPEndPoint(IP, GossipPort);
            }
        }
        public IPEndPoint ServiceEndPoint
        {
            get
            {
                return new IPEndPoint(IP, ServicePort);
            }
        }

        public void Update(MemberState state, byte generation, byte service = 0, ushort servicePort = 0)
        {
            State = state;
            Generation = generation;

            if (state == MemberState.Alive)
            {
                Service = service;
                ServicePort = servicePort;
            }

            Interlocked.Exchange(ref _gossipCounter, 0);
        }

        public void Update(MemberState state)
        {
            State = state;
            Interlocked.Exchange(ref _gossipCounter, 0);
        }

        public bool IsLaterGeneration(byte newGeneration)
        {
            return ((0 < (newGeneration - Generation)) && ((newGeneration - Generation) < 191))
                 || ((newGeneration - Generation) <= -191);
        }

        public bool IsStateSuperseded(MemberState newState)
        {
            // alive < suspicious < dead < left
            return State < newState;
        }

        public void WriteTo(Stream stream)
        {
            stream.WriteByte((byte)State);
            stream.WriteIPEndPoint(GossipEndPoint);
            stream.WriteByte(Generation);

            if (State == MemberState.Alive)
            {
                stream.WriteByte(Service);
                stream.WritePort(ServicePort);
            }

            Interlocked.Increment(ref _gossipCounter);
        }

        public static Member ReadFrom(Stream stream)
        {
            var newMember = new Member
            {
                State = stream.ReadMemberState(),
                IP = stream.ReadIPAddress(),
                GossipPort = stream.ReadPort(),
                Generation = (byte)stream.ReadByte(),
            };

            if (newMember.State == MemberState.Alive)
            {
                newMember.Service = (byte)stream.ReadByte();
                newMember.ServicePort = stream.ReadPort();
            }

            return newMember;
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