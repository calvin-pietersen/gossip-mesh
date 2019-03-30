using System;
using System.IO;
using System.Net;
using System.Threading;

namespace GossipMesh.Core
{
    public class Node
    {
        private long _gossipCounter = 0;

        public IPAddress IP { get; private set; }
        public ushort GossipPort { get; private set; }
        public NodeState State { get; private set; }
        public byte Generation { get; internal set; }
        public byte Service { get; private set; }
        public ushort ServicePort { get; private set; }
        internal long GossipCounter { get { return Interlocked.Read(ref _gossipCounter); } }

        internal Node(NodeEvent nodeEvent)
        {
            IP = nodeEvent.IP;
            GossipPort = nodeEvent.GossipPort;
            State = nodeEvent.State;
            Generation = nodeEvent.Generation;
            Service = nodeEvent.Service;
            ServicePort = nodeEvent.ServicePort;
        }

        public Node(NodeState state, IPAddress ip, ushort gossipPort, byte generation, byte service, ushort servicePort)
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

        internal void Update(NodeEvent nodeEvent)
        {
            State = nodeEvent.State;
            Generation = nodeEvent.Generation;

            if (nodeEvent.State == NodeState.Alive)
            {
                Service = nodeEvent.Service;
                ServicePort = nodeEvent.ServicePort;
            }

            Interlocked.Exchange(ref _gossipCounter, 0);
        }

        internal void Update(NodeState state)
        {
            State = state;
            Interlocked.Exchange(ref _gossipCounter, 0);
        }

        internal bool IsLaterGeneration(byte newGeneration)
        {
            return ((0 < (newGeneration - Generation)) && ((newGeneration - Generation) < 191))
                 || ((newGeneration - Generation) <= -191);
        }

        internal void WriteTo(Stream stream)
        {
            stream.WriteIPEndPoint(GossipEndPoint);
            stream.WriteByte((byte)State);
            stream.WriteByte(Generation);

            if (State == NodeState.Alive)
            {
                stream.WriteByte(Service);
                stream.WritePort(ServicePort);
            }

            Interlocked.Increment(ref _gossipCounter);
        }

        public override string ToString()
        {
            return string.Format("IP:{0} GossipPort:{1} State:{2} Generation:{3} Service:{4} ServicePort:{5}",
            IP,
            GossipPort,
            State,
            Generation,
            Service,
            ServicePort);
        }
    }
}