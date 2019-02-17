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

namespace GossipMesh.Example
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

            var loadBalancer = new RandomLoadBalancer();
            loadBalancer.RegisterServiceClientFactory(2, new GreeterClientFactory());

            var gossiper = StartGossiper(listenEndPoint, seeds, new IMemberListener[] { loadBalancer }, logger);
            var server = StartGrpcServer(listenEndPoint);

            while (true)
            {
                try
                {
                    Console.Write("Please enter your name: ");
                    var name = Console.ReadLine();

                    Greeter.GreeterClient client = loadBalancer.GetServiceClient<Greeter.GreeterClient>(2);

                    var request = new HelloRequest
                    {
                        Name = name
                    };

                    var response = client.SayHello(request);
                    Console.WriteLine("{response}", response.Message);
                }

                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                }
            }

            await server.ShutdownAsync();
        }

        private static Gossiper StartGossiper(IPEndPoint listenEndPoint, IPEndPoint[] seeds, IMemberListener[] memberListeners, ILogger logger)
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

            var gossiper = new Gossiper(options, new IMemberEventListener[0], memberListeners, logger);
            gossiper.Start();

            return gossiper;
        }

        private static Server StartGrpcServer(IPEndPoint serverEndPoint)
        {
            Server server = new Server
            {
                Services = { Greeter.BindService(new GreeterImpl()) },
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