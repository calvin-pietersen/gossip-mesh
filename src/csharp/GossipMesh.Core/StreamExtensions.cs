using System;
using System.IO;
using System.Net;

namespace GossipMesh.Core
{
    public static class StreamExtensions
    {
        public static IPAddress ReadIPAddress(this Stream stream)
        {
            if (stream.Position > stream.Length - 4)
            {
                throw new EndOfStreamException("could not read ip address from stream with less than 4 bytes remaining");
            }

            return new IPAddress(new byte[] { (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte() });
        }

        public static ushort ReadPort(this Stream stream)
        {
            if (stream.Position > stream.Length - 2)
            {
                throw new EndOfStreamException("could not read port from stream with less than 2 bytes remaining");
            }

            var bigByte = (byte)stream.ReadByte();
            var littleByte = (byte)stream.ReadByte();

            return BitConverter.IsLittleEndian ?
             BitConverter.ToUInt16(new byte[] { littleByte, bigByte }, 0) :
             BitConverter.ToUInt16(new byte[] { bigByte, littleByte }, 0);
        }

        public static IPEndPoint ReadIPEndPoint(this Stream stream)
        {
            if (stream.Position > stream.Length - 6)
            {
                throw new EndOfStreamException("could not read ip endpoint from stream with less than 6 bytes remaining");
            }

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
            stream.WriteByte((byte)(port >> 8));
            stream.WriteByte((byte)port);
        }

        public static void WritePort(this Stream stream, int port)
        {
            stream.WriteByte((byte)(port >> 8));
            stream.WriteByte((byte)port);
        }
    }
}