using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Adaptive.Aeron;
using Adaptive.Aeron.LogBuffer;
using Adaptive.Agrona;
using Adaptive.Agrona.Concurrent;
using GossipMesh.Core;
using GossipMesh.Examples.Common;
using GossipMesh.LoadBalancing;
using Microsoft.Extensions.Logging;

namespace AeronServer
{
    class Program
    {
        public static async Task Main(string[] args)
        {

            var listenPort = ushort.Parse(args[0]);
            var servicePort = ushort.Parse(args[1]);

            var seeds = args.Skip(2).Select(Utils.IPEndPointFromString).ToArray();
            var logger = Utils.CreateLogger<Program>();

            var gossiper = await StartGossiper(listenPort, servicePort, seeds, logger);

            const int fragmentLimitCount = 10;
            string channel = "aeron:udp?endpoint=localhost:" + servicePort;

            // A unique identifier for a stream within a channel. Stream ID 0 is reserved
            // for internal use and should not be used by applications.
            const int streamId = 10;

            Console.WriteLine("Subscribing to " + channel + " on stream Id " + streamId);


            // dataHandler method is called for every new datagram received
            var fragmentHandler = HandlerHelper.ToFragmentHandler((buffer, offset, length, header) =>
            {
                var data = new byte[length];
                buffer.GetBytes(offset, data);

                Console.WriteLine($"Received message ({Encoding.UTF8.GetString(data)}) to stream {streamId:D} from session {header.SessionId:x} term id {header.TermId:x} term offset {header.TermOffset:D} ({length:D}@{offset:D})");
            });

            // Create a context, needed for client connection to media driver
            // A separate media driver process need to run prior to running this application
            var ctx = new Aeron.Context();

            // Create an Aeron instance with client-provided context configuration, connect to the
            // media driver, and add a subscription for the given channel and stream using the supplied
            // dataHandler method, which will be called with new messages as they are received.
            // The Aeron and Subscription classes implement AutoCloseable, and will automatically
            // clean up resources when this try block is finished.
            using (var aeron = Aeron.Connect(ctx))
            using (var subscription = aeron.AddSubscription(channel, streamId))
            {
                IIdleStrategy idleStrategy = new BusySpinIdleStrategy();

                // Try to read the data from subscriber
                while (true)
                {
                    // poll delivers messages to the dataHandler as they arrive
                    // and returns number of fragments read, or 0
                    // if no data is available.
                    var fragmentsRead = subscription.Poll(fragmentHandler, fragmentLimitCount);
                    // Give the IdleStrategy a chance to spin/yield/sleep to reduce CPU
                    // use if no messages were received.
                    idleStrategy.Idle(fragmentsRead);
                }
            }
        }

        private static async Task<Gossiper> StartGossiper(ushort listenPort, ushort servicePort, IPEndPoint[] seeds, ILogger logger)
        {
            var options = new GossiperOptions
            {
                SeedMembers = seeds,
            };

            var gossiper = new Gossiper(listenPort, 0x04, servicePort, options, logger);
            await gossiper.StartAsync();

            return gossiper;
        }
    }
}
