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
            var listenEndPoint = IPEndPointFromString(args[0]);
            var seeds = args.Skip(1).Select(IPEndPointFromString).ToArray();

            var logger = CreateLogger();
            var server = StartGrpcServer(listenEndPoint, logger);
            var gossiper = StartGossiper(listenEndPoint, seeds, logger);

            Console.ReadKey();

            await server.ShutdownAsync();
        }

        private static ILogger CreateLogger()
        {
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new ConsoleLoggerProvider());
            return loggerFactory
                .CreateLogger<Program>();
        }
        private static Gossiper StartGossiper(IPEndPoint listenEndPoint, IPEndPoint[] seeds, ILogger logger)
        {
            var options = new GossiperOptions
            {
                MaxUdpPacketBytes = 508,
                ProtocolPeriodMilliseconds = 500,
                AckTimeoutMilliseconds = 250,
                NumberOfIndirectEndpoints = 2,
                ListenPort = (ushort)listenEndPoint.Port,
                MemberIP = listenEndPoint.Address,
                Service = (byte)2,
                ServicePort = (ushort)listenEndPoint.Port,
                SeedMembers = seeds
            };

            var gossiper = new Gossiper(options, Enumerable.Empty<IMemberEventsListener>(),Enumerable.Empty<IMemberListener>(), logger);
            gossiper.Start();

            return gossiper;
        }
        private static Server StartGrpcServer(IPEndPoint serverEndPoint, ILogger logger)
        {
            Server server = new Server
            {
                Services = { Helloworld.Greeter.BindService(new GreeterImpl(logger)) },
                Ports = { new ServerPort(serverEndPoint.Address.ToString(), serverEndPoint.Port, ServerCredentials.Insecure) }
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