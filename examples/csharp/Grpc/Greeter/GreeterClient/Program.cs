using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using GossipMesh.Core;
using GossipMesh.LoadBalancing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Grpc.Core;
using Helloworld;
using System.Diagnostics;
using System.Collections.Generic;
using GossipMesh.Examples.Common;

namespace GreeterClient
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var listenPort = ushort.Parse(args[0]);
            var seeds = args.Skip(1).Select(Utils.IPEndPointFromString).ToArray();

            var logger = Utils.CreateLogger<Program>();

            var loadBalancer = CreateLoadBalancer();
            var gossiper = await StartGossiper(listenPort, seeds, new IMemberListener[] { loadBalancer }, logger);

            var stopwatch = new Stopwatch();
            while (true)
            {
                try
                {
                    Console.Write("Please enter your name: ");
                    var name = Console.ReadLine();
                    stopwatch.Restart();

                    var serviceClient = loadBalancer.GetServiceClient<GreeterServiceClient>(2);

                    var request = new HelloRequest{ Name = name};
                    var response = await serviceClient.Client.SayHelloAsync(request).ResponseAsync.ConfigureAwait(false);

                    stopwatch.Stop();
                    Console.WriteLine($"Response: {response.Message} From: {serviceClient.ServiceEndPoint} TimeTaken: {stopwatch.Elapsed.TotalMilliseconds}ms");
                }

                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                }
            }
        }

        private static RandomLoadBalancer CreateLoadBalancer()
        {
            var serviceClientFactories = new Dictionary<byte, IServiceClientFactory>
            {
                { 2, new GreeterServiceClientFactory()}
            };

            return new RandomLoadBalancer(serviceClientFactories);
        }

        private static async Task<Gossiper> StartGossiper(ushort listenPort, IPEndPoint[] seeds, IMemberListener[] memberListeners, ILogger logger)
        {
            var options = new GossiperOptions
            {
                SeedMembers = seeds,
                MemberListeners = memberListeners
            };

            var gossiper = new Gossiper(listenPort, 0x03, listenPort, options, logger);
            await gossiper.StartAsync();

            return gossiper;
        }
    }
}