using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using GossipMesh.Core;
using GossipMesh.Examples.Common;
using GossipMesh.LoadBalancing;
using Microsoft.Extensions.Logging;
using NetMQ;
using NetMQ.Sockets;

namespace ZeroMQClient
{
    class Program
    {
        private const int RequestTimeout = 1000; // ms
        private const int MaxRetries = 50; // Before we abandon

        static async Task Main(string[] args)
        {
            var listenPort = ushort.Parse(args[0]);

            var seeds = args.Skip(1).Select(Utils.IPEndPointFromString).ToArray();
            var logger = Utils.CreateLogger<Program>();

            var loadbalancer = CreateLoadBalancer();
            var gossiper = await StartGossiper(listenPort, seeds, new IMemberListener[] { loadbalancer }, logger);

            var stopwatch = new Stopwatch();
            while(true)
            {
                Console.Write("Please enter your name: ");
                var name = Console.ReadLine();
                stopwatch.Restart();

                var serviceClient = loadbalancer.GetServiceClient<ZeroMQServiceClient>(6);

                serviceClient.Client.SendFrame(name);
                var response = serviceClient.Client.ReceiveFrameString();
                
                stopwatch.Stop();
                Console.WriteLine($"Response: {response} From: {serviceClient.ServiceEndPoint} TimeTaken: {stopwatch.Elapsed.TotalMilliseconds}ms");
            }
        }

        private static RandomLoadBalancer CreateLoadBalancer()
        {
            var serviceClientFactories = new Dictionary<byte, IServiceClientFactory>
            {
                { 6, new ZeroMQServiceClientFactory()}
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

            var gossiper = new Gossiper(listenPort, 0x07, 0, options, logger);
            await gossiper.StartAsync();

            return gossiper;
        }
    }
}