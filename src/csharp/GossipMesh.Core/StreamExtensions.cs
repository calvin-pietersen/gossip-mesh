using System;
using System.IO;
using System.Net;

namespace GossipMesh.Core
{
    public static class StreamExtensions
    {
        public static IPAddress ReadIPAddress(this Stream stream)
        {
            return new IPAddress(new byte[] { (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte() });
        }

        public static ushort ReadPort(this Stream stream)
        {
            return BitConverter.ToUInt16(new byte[] { (byte)stream.ReadByte(), (byte)stream.ReadByte() }, 0);
        }
    }
}