using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GossipMesh.Core
{
    public class GossiperOptions
    {
        public int MaxUdpPacketBytes { get; set; } = 508;
        private int _protocolPeriodMilliseconds = 250;
        private int _ackTimeoutMilliseconds = 125;
        private int _deadTimeoutMilliseconds = 1000;
        public int ProtocolPeriodMilliseconds
        {
            get
            {
                return _protocolPeriodMilliseconds;
            }
            set
            {
                _protocolPeriodMilliseconds = value;
                _ackTimeoutMilliseconds = value / 2;
                _deadTimeoutMilliseconds = value * 5;
            }
        }
        public int AckTimeoutMilliseconds { get { return _ackTimeoutMilliseconds; } }
        public int DeadTimeoutMilliseconds { get { return _deadTimeoutMilliseconds; } }
        public int DeadCoolOffMilliseconds { get; set; } = 30000;
        public int PruneTimeoutMilliseconds { get; set; } = 60000;
        public int FanoutFactor { get; set; } = 3;
        public int NumberOfIndirectEndpoints { get; set; } = 3;
        public IPEndPoint[] SeedMembers { get; set; } = new IPEndPoint[0];
        public IEnumerable<IMemberListener> MemberListeners { get; set; } = Enumerable.Empty<IMemberListener>();
    }
}