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
            var listenPort = ushort.Parse(args[0]);

            var seeds = args.Skip(1).Select(s =>
            {
                var endpoint = s.Split(":");
                return new IPEndPoint(IPAddress.Parse(endpoint[0]), int.Parse(endpoint[1]));
            }).ToArray();
      
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new ConsoleLoggerProvider());
            var logger = loggerFactory
                .CreateLogger<Program>();

            var options = new ServerOptions
            {
                MaxUdpPacketBytes = 508,                
                ProtocolPeriodMilliseconds = 500,
                AckTimeoutMilliseconds = 200,
                NumberOfIndirectEndpoints = 2,
                ListenPort = listenPort,
                Service = 1,
                ServicePort = 8080,
                SeedMembers = seeds
            };

            var server = new GossipMesh.Core.Server(options, logger);
            server.Start();

            while (true)
            {
                await Task.Delay(10000).ConfigureAwait(false);
            }
        }
    }
}
