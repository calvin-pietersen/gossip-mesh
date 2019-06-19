using System;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace AeronClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var listenPort = ushort.Parse(args[0]);

            var seeds = args.Skip(1).Select(Utils.IPEndPointFromString).ToArray();
            var logger = Utils.CreateLogger<Program>();

            var loadbalancer = CreateLoadBalancer();
            var gossiper = await StartGossiper(listenPort, seeds, new IMemberListener[] { loadbalancer }, logger);

            // Allocate enough buffer size to hold maximum message length
            // The UnsafeBuffer class is part of the Agrona library and is used for efficient buffer management
            var buffer = new UnsafeBuffer(BufferUtil.AllocateDirectAligned(512, BitUtil.CACHE_LINE_LENGTH));

            var stopwatch = new Stopwatch();
            while(true)
            {
                try
                {
                    Console.Write("Please enter your name: ");
                    string message = Console.ReadLine();

                    stopwatch.Restart();
                    var messageBytes = Encoding.UTF8.GetBytes(message);
                    buffer.PutBytes(0, messageBytes);

                    var client = loadbalancer.GetServiceClient<AeronServiceClient>(4);

                    // Try to publish the buffer. 'offer' is a non-blocking call.
                    // If it returns less than 0, the message was not sent, and the offer should be retried.
                    var result = client.Publication.Offer(buffer, 0, messageBytes.Length);

                    if (result < 0L)
                    {
                        switch (result)
                        {
                            case Publication.BACK_PRESSURED:
                                Console.WriteLine(" Offer failed due to back pressure");
                                break;
                            case Publication.NOT_CONNECTED:
                                Console.WriteLine(" Offer failed because publisher is not connected to subscriber");
                                break;
                            case Publication.ADMIN_ACTION:
                                Console.WriteLine("Offer failed because of an administration action in the system");
                                break;
                            case Publication.CLOSED:
                                Console.WriteLine("Offer failed publication is closed");
                                break;
                            default:
                                Console.WriteLine(" Offer failed due to unknown reason");
                                break;
                        }
                    }
                    else
                    {
                        stopwatch.Stop();
                        Console.WriteLine($"Message Sent TimeTaken: {stopwatch.Elapsed.TotalMilliseconds}ms");
                    }
                }

                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private static RandomLoadBalancer CreateLoadBalancer()
        {
            var serviceClientFactories = new Dictionary<byte, IServiceClientFactory>
            {
                { 4, new AeronServiceClientFactory()}
            };

            return new RandomLoadBalancer(serviceClientFactories);
        }

        private static async Task<Gossiper> StartGossiper(ushort listenPort, IPEndPoint[] seeds, IEnumerable<IMemberListener> listeners, ILogger logger)
        {
            var options = new GossiperOptions
            {
                SeedMembers = seeds,
                MemberListeners = listeners
            };

            var gossiper = new Gossiper(listenPort, 0x05, 0, options, logger);
            await gossiper.StartAsync();

            return gossiper;
        }
    }
}
