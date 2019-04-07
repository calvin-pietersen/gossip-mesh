using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GossipMesh.Core;
using GossipMesh.Examples.Common;
using Microsoft.Extensions.Logging;
using NetMQ;
using NetMQ.Sockets;

namespace ZeroMQServer
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var listenPort = ushort.Parse(args[0]);

            var seeds = args.Skip(1).Select(Utils.IPEndPointFromString).ToArray();
            var logger = Utils.CreateLogger<Program>();

            var gossiper = await StartGossiper(listenPort, seeds, logger);

            using (var response = new ResponseSocket())
            {
                string address = "0.0.0.0";

                Console.WriteLine("Binding tcp://{0}:{1}", address, listenPort);
                response.Bind($"tcp://{address}:{listenPort}");

                while (true)
                {
                    bool hasMore;
                    string msg = response.ReceiveFrameString(out hasMore);

                    Console.WriteLine("Msg received! {0}", msg);
                    response.SendFrame(msg, hasMore);
                }
            }
        }

        private static async Task<Gossiper> StartGossiper(ushort listenPort, IPEndPoint[] seeds, ILogger logger)
        {
            var options = new GossiperOptions
            {
                SeedMembers = seeds,
            };

            var gossiper = new Gossiper(listenPort, 0x06, listenPort, options, logger);
            await gossiper.StartAsync();

            return gossiper;
        }
    }
}