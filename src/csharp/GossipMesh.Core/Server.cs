using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GossipMesh.Core
{
    public class Server
    {
        private readonly int _listenPort;
        private readonly int _protocolPeriodMs;

        private readonly ILogger _logger;

        public Server(int listenPort, int protocolPeriodMs, ILogger logger)
        {
            _listenPort = listenPort;
            _protocolPeriodMs = protocolPeriodMs;
            _logger = logger;
        }

        public async Task StartAsync()
        {
            _logger.LogInformation("Starting Gossip.Mesh server");
            // recieve
            Task.Run(async () => StartListener().ConfigureAwait(false)).ConfigureAwait(false);

            // ping
            Task.Run(async () => GossipPump().ConfigureAwait(false)).ConfigureAwait(false);
        }

        public async Task GossipPump()
        {
            while (true)
            {
                // ping
                _logger.LogInformation("Gossip.Mesh ping {Seed}", "seed");
                var udpClient = new UdpClient("localhost", _listenPort);
                try
                {
                    var bytes = new byte[512];
                    bytes[0] = 0x01;

                    await udpClient.SendAsync(bytes, 512).ConfigureAwait(false);
                }

                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                finally
                {
                    udpClient.Close();
                }
                await Task.Delay(_protocolPeriodMs).ConfigureAwait(false);
            }
        }

        public async Task StartListener()
        {
            var udpServer = new UdpClient(_listenPort);

            try
            {
                while (true)
                {
                    // recieve
                    var request = await udpServer.ReceiveAsync().ConfigureAwait(false);
                    _logger.LogInformation("Gossip.Mesh recieved {MessageType} from {Member}", request.Buffer[0], request.RemoteEndPoint);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            finally
            {
                udpServer.Close();
            }
        }
    }
}