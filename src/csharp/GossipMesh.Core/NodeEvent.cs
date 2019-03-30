using System;
using System.IO;
using System.Net;
using System.Threading;

namespace GossipMesh.Core
{
    public class NodeEvent
    {
        public IPEndPoint SenderGossipEndPoint;
        public DateTime ReceivedDateTime; 

        public NodeState State { get; private set; }
        public IPAddress IP { get; private set; }
        public ushort GossipPort { get; private set; }
        public byte Generation { get; private set; }
        public byte Service { get; private set; }
        public ushort ServicePort { get; private set; }

        public IPEndPoint GossipEndPoint 
        {
            get { return new IPEndPoint(IP, GossipPort); }
        }

        private NodeEvent()
        {
        }

        internal NodeEvent(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, IPAddress ip, ushort gossipPort, NodeState state, byte generation)
        {
            SenderGossipEndPoint = senderGossipEndPoint;
            ReceivedDateTime = receivedDateTime;

            IP = ip;
            GossipPort = gossipPort;
            State = state;
            Generation = generation;
        }

        internal NodeEvent(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Node node)
        {
            SenderGossipEndPoint = senderGossipEndPoint;
            ReceivedDateTime = receivedDateTime;

            IP = node.IP;
            GossipPort = node.GossipPort;
            State = node.State;
            Generation = node.Generation;
            Service = node.Service;
            ServicePort = node.ServicePort;
        }

        internal static NodeEvent ReadFrom(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Stream stream, bool isSender = false)
        {
            if (stream.Position >= stream.Length)
            {
                return null;
            }

            var nodeEvent = new NodeEvent
            {
                SenderGossipEndPoint = senderGossipEndPoint,
                ReceivedDateTime = receivedDateTime,

                IP = isSender ? senderGossipEndPoint.Address : stream.ReadIPAddress(),
                GossipPort = isSender ? (ushort)senderGossipEndPoint.Port : stream.ReadPort(),
                State = isSender ? NodeState.Alive : stream.ReadNodeState(),
                Generation = (byte)stream.ReadByte(),
            };

            if (nodeEvent.State == NodeState.Alive)
            {
                nodeEvent.Service = (byte)stream.ReadByte();
                nodeEvent.ServicePort = stream.ReadPort();
            }

            return nodeEvent;
        }
    
        public override string ToString()
        {
            return string.Format("Sender:{0} Received:{1} IP:{2} GossipPort:{3} State:{4} Generation:{5} Service:{6} ServicePort:{7}",
            SenderGossipEndPoint,
            ReceivedDateTime,
            IP,
            GossipPort,
            State,
            Generation,
            Service,
            ServicePort);
        }

        public bool Equal(NodeEvent nodeEvent)
        {
            return nodeEvent != null &&
                    IP.Equals(nodeEvent.IP) &&
                    GossipPort == nodeEvent.GossipPort &&
                    State == nodeEvent.State &&
                    Generation == nodeEvent.Generation &&
                    Service == nodeEvent.Service &&
                    ServicePort == nodeEvent.ServicePort;
        }

        public bool NotEqual(NodeEvent nodeEvent)
        {
            return !Equal(nodeEvent);
        }
    }
}