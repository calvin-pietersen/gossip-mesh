using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using GossipMesh.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Grpc.Core;
using Greeter;

namespace GreeterServer
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var listenPort = ushort.Parse(args[0]);
            var seeds = args.Skip(1).Select(IPEndPointFromString).ToArray();

            var logger = CreateLogger();
            var server = StartGrpcServer(listenPort, logger);
            var gossiper = await StartGossiper(listenPort, seeds, logger);

            await Task.Delay(-1);
            await server.ShutdownAsync();
        }

        private static ILogger CreateLogger()
        {
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new ConsoleLoggerProvider());
            return loggerFactory
                .CreateLogger<Program>();
        }

        private static async Task<Gossiper> StartGossiper(ushort listenPort, IPEndPoint[] seeds, ILogger logger)
        {
            var options = new GossiperOptions
            {
                SeedMembers = seeds,
            };

            var gossiper = new Gossiper(listenPort, 0x02, listenPort, options, logger);
            await gossiper.StartAsync();

            return gossiper;
        }

        private static Server StartGrpcServer(ushort listenPort, ILogger logger)
        {
            Server server = new Server
            {
                Services = { Helloworld.Greeter.BindService(new GreeterImpl(logger)) },
                Ports = { new ServerPort("0.0.0.0", listenPort, ServerCredentials.Insecure) }
            };

            server.Start();
            return server;
        }

        private static IPEndPoint IPEndPointFromString(string ipEndPointString)
        {
            var endpoint = ipEndPointString.Split(":");
            return new IPEndPoint(IPAddress.Parse(endpoint[0]), int.Parse(endpoint[1]));
        }
    }
}