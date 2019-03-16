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
        private int _protocolPeriodMilliseconds = 1000;
        private int _ackTimeoutMilliseconds = 500;
        private int _deadTimeoutMilliseconds = 5000;
        private int _deadCoolOffMilliseconds = 30000;
        private int _pruneTimeoutMilliseconds = 60000;
        public int MaxUdpPacketBytes { get; set; } = 508;
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
                _deadCoolOffMilliseconds = value * 300;
                _pruneTimeoutMilliseconds = value * 600;
            }
        }
        public int AckTimeoutMilliseconds { get { return _ackTimeoutMilliseconds; } }
        public int DeadTimeoutMilliseconds { get { return _deadTimeoutMilliseconds; } }
        public int DeadCoolOffMilliseconds { get { return _deadCoolOffMilliseconds; } }
        public int PruneTimeoutMilliseconds { get { return _pruneTimeoutMilliseconds; } }
        public int FanoutFactor { get; set; } = 5;
        public int NumberOfIndirectEndpoints { get; set; } = 3;
        public IPEndPoint[] SeedMembers { get; set; } = new IPEndPoint[0];
        public IEnumerable<IMemberListener> MemberListeners { get; set; } = Enumerable.Empty<IMemberListener>();
    }
}