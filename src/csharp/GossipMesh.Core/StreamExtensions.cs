using System;
using System.IO;
using System.Net;

namespace GossipMesh.Core
{
    public static class StreamExtensions
    {
        public static MessageType ReadMessageType(this Stream stream)
        {
            return (MessageType)stream.ReadByte();
        }

        public static NodeState ReadNodeState(this Stream stream)
        {
            return (NodeState)stream.ReadByte();
        }

        public static IPAddress ReadIPAddress(this Stream stream)
        {
            return new IPAddress(new byte[] { (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte() });
        }

        public static ushort ReadPort(this Stream stream)
        {
            var bigByte = (byte)stream.ReadByte();
            var littleByte = (byte)stream.ReadByte();

            return BitConverter.IsLittleEndian ?
             BitConverter.ToUInt16(new byte[] { littleByte, bigByte }, 0) :
             BitConverter.ToUInt16(new byte[] { bigByte, littleByte }, 0);
        }

        public static IPEndPoint ReadIPEndPoint(this Stream stream)
        {
            return new IPEndPoint(stream.ReadIPAddress(), stream.ReadPort());
        }

        public static void WriteIPAddress(this Stream stream, IPAddress ipAddress)
        {
            if (ipAddress == null)
            {
                throw new ArgumentNullException(nameof(ipAddress));
            }

            stream.Write(ipAddress.GetAddressBytes(), 0, 4);
        }

        public static void WritePort(this Stream stream, ushort port)
        {
            stream.WriteByte((byte)(port >> 8));
            stream.WriteByte((byte)port);
        }

        public static void WriteIPEndPoint(this Stream stream, IPEndPoint ipEndPoint)
        {
            if (ipEndPoint == null)
            {
                throw new ArgumentNullException(nameof(ipEndPoint));
            }            

            stream.WriteIPAddress(ipEndPoint.Address);
            stream.WritePort((ushort)ipEndPoint.Port);
        }
    }
}