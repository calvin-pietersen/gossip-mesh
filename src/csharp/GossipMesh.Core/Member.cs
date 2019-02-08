using System;
using System.IO;
using System.Net;
using System.Threading;

namespace GossipMesh.Core
{
    public class Member
    {
        private long _gossipCounter = 0;

        public MemberState State { get; private set; }
        public IPAddress IP { get; private set; }
        public ushort GossipPort { get; private set; }
        public byte Generation { get; internal set; }
        public byte Service { get; private set; }
        public ushort ServicePort { get; private set; }
        internal long GossipCounter { get { return Interlocked.Read(ref _gossipCounter); } }

        internal Member(MemberEvent memberEvent)
        {
            State = memberEvent.State;
            IP = memberEvent.IP;
            GossipPort = memberEvent.GossipPort;
            Generation = memberEvent.Generation;
            Service = memberEvent.Service;
            ServicePort = memberEvent.ServicePort;
        }

        public Member(MemberState state, IPAddress ip, ushort gossipPort, byte generation, byte service, ushort servicePort)
        {
            State = state;
            IP = ip;
            GossipPort = gossipPort;
            Generation = generation;
            Service = service;
            ServicePort = servicePort;
        }

        internal IPEndPoint GossipEndPoint
        {
            get
            {
                return new IPEndPoint(IP, GossipPort);
            }
        }

        internal void Update(MemberEvent memberEvent)
        {
            State = memberEvent.State;
            Generation = memberEvent.Generation;

            if (memberEvent.State == MemberState.Alive)
            {
                Service = memberEvent.Service;
                ServicePort = memberEvent.ServicePort;
            }

            Interlocked.Exchange(ref _gossipCounter, 0);
        }

        internal void Update(MemberState state)
        {
            State = state;
            Interlocked.Exchange(ref _gossipCounter, 0);
        }

        internal bool IsLaterGeneration(byte newGeneration)
        {
            return ((0 < (newGeneration - Generation)) && ((newGeneration - Generation) < 191))
                 || ((newGeneration - Generation) <= -191);
        }

        internal bool IsStateSuperseded(MemberState newState)
        {
            // alive < suspicious < dead < left
            return State < newState;
        }

        internal void WriteTo(Stream stream)
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