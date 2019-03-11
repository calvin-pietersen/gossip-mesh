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
        private int _protocolPeriodMilliseconds;
        private int _ackTimeoutMilliseconds;
        public int MaxUdpPacketBytes { get; set; }
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
            }
        }
        public int AckTimeoutMilliseconds { get { return _ackTimeoutMilliseconds; } }
        public int NumberOfIndirectEndpoints { get; set; }
        public ushort ListenPort { get; set; }
        public byte Service { get; set; }
        public ushort ServicePort { get; set; }
        public IPEndPoint[] SeedMembers { get; set; }
    }
}