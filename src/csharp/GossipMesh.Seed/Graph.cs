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

        public class Link : IEquatable<Link>
        {
            public IPEndPoint Source { get; set; }
            public IPEndPoint Target { get; set; }

            public bool Equals(Link other)
            {
                return ((Source.Address.Equals(other.Source.Address) && Source.Port == other.Source.Port) &&
                    (Target.Address.Equals(other.Target.Address) && Target.Port == other.Target.Port)) ||

                    (((Source.Address.Equals(other.Target.Address) && Source.Port == other.Target.Port) &&
                    (Target.Address.Equals(other.Source.Address) && Target.Port == other.Source.Port)));
            }

            public override int GetHashCode()
            {

                if (BitConverter.ToUInt32(Source.Address.GetAddressBytes()) >= BitConverter.ToUInt32(Target.Address.GetAddressBytes()) && Source.Port >= Target.Port)
                {
                    return BitConverter.ToUInt32(Source.Address.GetAddressBytes()).GetHashCode() ^ Source.Port.GetHashCode();
                }

                else
                {
                    return BitConverter.ToUInt32(Target.Address.GetAddressBytes()).GetHashCode() ^ Target.Port.GetHashCode();
                }
            }
        }
    }
}