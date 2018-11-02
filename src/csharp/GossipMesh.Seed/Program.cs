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
            var listenPort = int.Parse(args[0]);

            var seeds = args.Skip(1).Select(s =>
            {
                var endpoint = s.Split(":");
                return new IPEndPoint(IPAddress.Parse(endpoint[0]), int.Parse(endpoint[1]));
            }).ToList();

            var logger = new LoggerFactory()
                .AddConsole()
                .AddDebug()
                .CreateLogger<Program>();
      
            // var loggerFactory = new LoggerFactory();
            // loggerFactory.AddProvider(new ConsoleLoggerProvider());
            // var logger = loggerFactory
            //     .CreateLogger<Program>();

            var server = new GossipMesh.Core.Server(listenPort, 500, 200, logger, seeds);
            server.Start();

            while (true)
            {
                //logger.LogInformation("seed node doing random things");
                await Task.Delay(10000).ConfigureAwait(false);
            }
        }
    }
}
