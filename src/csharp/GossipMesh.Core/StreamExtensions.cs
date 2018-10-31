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

        public static IPEndPoint ReadIPEndPoint(this Stream stream)
        {
            return new IPEndPoint(stream.ReadIPAddress(), stream.ReadPort());
        }

        public static void WriteIPEndPoint(this Stream stream, IPEndPoint ipEndPoint)
        {
            stream.WriteIPAddress(ipEndPoint.Address);
            stream.WritePort(ipEndPoint.Port);
        }

        public static void WriteIPAddress(this Stream stream, IPAddress ipAddress)
        {
            stream.Write(ipAddress.GetAddressBytes(), 0, 4);
        }

        public static void WritePort(this Stream stream, ushort port)
        {
            stream.WriteByte((byte)port);
            stream.WriteByte((byte)(port >> 8));
        }

        public static void WritePort(this Stream stream, int port)
        {
            stream.WriteByte((byte)port);
            stream.WriteByte((byte)(port >> 8));
        }
    }
}