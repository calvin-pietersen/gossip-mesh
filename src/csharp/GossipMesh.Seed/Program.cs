using System;
using System.Threading.Tasks;
using GossipMesh.Core;
using Microsoft.Extensions.Logging;

namespace GossipMesh.Seed
{
    class Program
    {
        public static async Task Main(string[] args)
        {

            ILoggerFactory loggerFactory = new LoggerFactory()
                .AddConsole()
                .AddDebug();

            ILogger logger = loggerFactory.CreateLogger<Program>();

            var server = new GossipMesh.Core.Server(11000, 1000, logger);
            server.StartAsync();

            while (true)
            {
                logger.LogInformation("seed node doing random things");
                await Task.Delay(10000);
            }
        }
    }
}
