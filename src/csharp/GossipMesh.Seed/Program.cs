using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using GossipMesh.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GossipMesh.Seed
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var listenEndPoint = IPEndPointFromString(args[0]);

            var seeds = args.Skip(1).Select(IPEndPointFromString).ToArray();
      
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new ConsoleLoggerProvider());
            var logger = loggerFactory
                .CreateLogger<Program>();

            var LoadBalancer = new LoadBalancer();

            var options = new GossiperOptions
            {
                MaxUdpPacketBytes = 508,
                ProtocolPeriodMilliseconds = 200,
                AckTimeoutMilliseconds = 80,
                NumberOfIndirectEndpoints = 2,
                ListenPort = (ushort)listenEndPoint.Port,
                MemberIP = listenEndPoint.Address,
                Service = (byte)1,
                ServicePort = (ushort)8080,
                SeedMembers = seeds,
                StateListener = LoadBalancer
            };

            var gossiper = new Gossiper(options, logger);
            gossiper.Start();

            while (true)
            {
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        public static IPEndPoint IPEndPointFromString(string ipEndPointString)
        {
                var endpoint = ipEndPointString.Split(":");
                return new IPEndPoint(IPAddress.Parse(endpoint[0]), int.Parse(endpoint[1]));
        }
    }
}
