using System;
using System.Net;
using GossipMesh.Core;

namespace GossipMesh.Seed
{
    public class Graph
    {
        public Node[] Nodes { get; set; }
        public Link[] Links { get; set; }

        public class Node
        {
            public IPEndPoint Id { get; set; }
            public IPAddress Ip { get; set; }
            public MemberState State { get; set; }
            public byte Generation { get; set; }
            public byte Service { get; set; }
            public ushort ServicePort { get; set; }
        }

        public class Link
        {
            public IPEndPoint Source { get; set; }
            public IPEndPoint Target { get; set; }

            internal static string GetLinkId(IPEndPoint source, IPEndPoint target)
            {

                if (BitConverter.ToUInt32(source.Address.GetAddressBytes()) >= BitConverter.ToUInt32(target.Address.GetAddressBytes()) && source.Port >= target.Port)
                {
                    return source.ToString() + target.ToString();
                }

                else
                {
                    return target.ToString() + source.ToString();
                }
            }
        }
    }
}