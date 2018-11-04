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
    public class ServerOptions
    {
        public int ProtocolPeriodMilliseconds { get; set; }
        public int AckTimeoutMilliseconds { get; set; }
        public int MaxUdpPacketBytes {get; set; }
        public ushort ListenPort { get; set; }
        public byte Service { get; set; }
        public ushort ServicePort { get; set; }
        public IPEndPoint[] SeedMembers { get; set; }
    }
}