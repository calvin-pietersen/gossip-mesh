using System;
using System.IO;
using System.Net;
using System.Threading;

namespace GossipMesh.Core
{
    public class MemberEvent
    {
        public IPEndPoint SenderGossipEndPoint;
        public DateTime ReceivedDateTime; 

        public MemberState State { get; private set; }
        public IPAddress IP { get; private set; }
        public ushort GossipPort { get; private set; }
        public byte Generation { get; private set; }
        public byte Service { get; private set; }
        public ushort ServicePort { get; private set; }

        public IPEndPoint GossipEndPoint 
        {
            get { return new IPEndPoint(IP, GossipPort); }
        }

        private MemberEvent()
        {
        }

        internal MemberEvent(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Member member)
        {
            SenderGossipEndPoint = senderGossipEndPoint;
            ReceivedDateTime = receivedDateTime;

            State = member.State;
            IP = member.IP;
            GossipPort = member.GossipPort;
            Generation = member.Generation;
            Service = member.Service;
            ServicePort = member.ServicePort;
        }

        internal static MemberEvent ReadFrom(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Stream stream)
        {
            var memberEvent = new MemberEvent
            {
                SenderGossipEndPoint = senderGossipEndPoint,
                ReceivedDateTime = receivedDateTime,

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
            return string.Format("Sender:{0} Received:{1} State:{2} IP:{3} GossipPort:{4} Generation:{5} Service:{6} ServicePort:{7}",
            SenderGossipEndPoint,
            ReceivedDateTime,
            State,
            IP,
            GossipPort,
            Generation,
            Service,
            ServicePort);
        }

        public bool Equal(MemberEvent memberEvent)
        {
            return memberEvent != null &&
                    State == memberEvent.State &&
                    IP.Equals(memberEvent.IP) &&
                    GossipPort == memberEvent.GossipPort &&
                    Generation == memberEvent.Generation &&
                    Service == memberEvent.Service &&
                    ServicePort == memberEvent.ServicePort;
        }

        public bool NotEqual(MemberEvent memberEvent)
        {
            return !Equal(memberEvent);
        }
    }
}