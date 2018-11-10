using System;
using System.IO;
using System.Net;
using NUnit.Framework;

namespace GossipMesh.Core.Tests
{
    [TestFixture]
    public class StreamExtensionsTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void ReadIPAddress_WithNullStream_ThrowsNullReferenceException()
        {
            // arrange
            Stream stream = null;

            // assert
            Assert.Throws(typeof(NullReferenceException), () => stream.ReadIPAddress());
        }

        [Test]
        public void ReadIPAddress_WithEmptyStream_ThrowsEndOfStreamException()
        {
            // arrange
            var emptyBuffer = new byte[0];
            Stream stream = new MemoryStream(emptyBuffer, false);

            // assert
            Assert.Throws(typeof(EndOfStreamException), () => stream.ReadIPAddress(), "could not read ip address from stream with less than 4 bytes remaining");
        }

        [Test]
        public void ReadIPAddress_WithPartiallyCompleteStream_ThrowsEndOfStreamException()
        {
            // arrange
            var adressBuffer = new byte[] { 192, 168 };
            Stream stream = new MemoryStream(adressBuffer, false);

            // assert
            Assert.Throws(typeof(EndOfStreamException), () => stream.ReadIPAddress(), "could not read ip address from stream with less than 4 bytes remaining");
        }

        [Test]
        public void ReadIPAddress_WithCompleteStream_ReturnsIPAddress()
        {
            // arrange
            var adressBuffer = new byte[] { 192, 168, 0, 1 };
            Stream stream = new MemoryStream(adressBuffer, false);

            var expected = new IPAddress(adressBuffer);

            // act
            var actual = stream.ReadIPAddress();

            // assert
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ReadPort_WithNullStream_ThrowsNullReferenceException()
        {
            // arrange
            Stream stream = null;

            // assert
            Assert.Throws(typeof(NullReferenceException), () => stream.ReadPort());
        }

        [Test]
        public void ReadPort_WithEmptyStream_ThrowsEndOfStreamException()
        {
            // arrange
            var emptyBuffer = new byte[0];
            Stream stream = new MemoryStream(emptyBuffer, false);

            // assert
            Assert.Throws(typeof(EndOfStreamException), () => stream.ReadPort(), "could not read port from stream with less than 2 bytes remaining");
        }

        [Test]
        public void ReadPort_WithPartiallyCompleteStream_ThrowsEndOfStreamException()
        {
            // arrange
            var portBuffer = new byte[] { 255 };
            Stream stream = new MemoryStream(portBuffer, false);

            // assert
            Assert.Throws(typeof(EndOfStreamException), () => stream.ReadPort(), "could not read port from stream with less than 2 bytes remaining");
        }

        [Test]
        public void ReadPort_WithCompleteStream_ReturnsPort()
        {
            // arrange
            var portBuffer = new byte[] { 255, 255 };
            Stream stream = new MemoryStream(portBuffer, false);

            var expected = ushort.MaxValue;

            // act
            var actual = stream.ReadPort();

            // assert
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ReadIPEndPoint_WithNullStream_ThrowsNullReferenceException()
        {
            // arrange
            Stream stream = null;

            // assert
            Assert.Throws(typeof(NullReferenceException), () => stream.ReadIPEndPoint());
        }

        [Test]
        public void ReadIPEndPoint_WithEmptyStream_ThrowsEndOfStreamException()
        {
            // arrange
            var emptyBuffer = new byte[0];
            Stream stream = new MemoryStream(emptyBuffer, false);

            // assert
            Assert.Throws(typeof(EndOfStreamException), () => stream.ReadIPEndPoint(), "could not read ip endpoint from stream with less than 6 bytes remaining");
        }

        [Test]
        public void ReadIPEndPoint_WithPartiallyCompleteStream_ThrowsEndOfStreamException()
        {
            // arrange
            var ipEndPointBuffer = new byte[] { 192, 168 };
            Stream stream = new MemoryStream(ipEndPointBuffer, false);

            // assert
            Assert.Throws(typeof(EndOfStreamException), () => stream.ReadIPEndPoint(), "could not read ip endpoint from stream with less than 6 bytes remaining");
        }

        [Test]
        public void ReadIPEndPoint_WithCompleteStream_ReturnsIPEndPoint()
        {
            // arrange
            var ipEndPointBuffer = new byte[] { 192, 168, 0, 1, 255, 255 };
            Stream stream = new MemoryStream(ipEndPointBuffer, false);

            var expectedAddress = new IPAddress(new byte[] { 192, 168, 0, 1 });
            var expected = new IPEndPoint(expectedAddress, ushort.MaxValue);

            // act
            var actual = stream.ReadIPEndPoint();

            // assert
            Assert.AreEqual(expected, actual);
        }
    }
}