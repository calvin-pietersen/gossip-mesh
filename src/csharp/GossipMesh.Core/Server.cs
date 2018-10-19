using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GossipMesh.Core
{
    public class Server
    {
        private readonly IPEndPoint _localEndpoint;
        private readonly int _protocolPeriodMs;

        private readonly ILogger _logger;

        public Server(int listenPort, int protocolPeriodMs, ILogger logger)
        {
            _localEndpoint = new IPEndPoint(IPAddress.Any, listenPort);
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
            var udpClient = CreateUdpClient();

            try
            {
                while (true)
                {
                    // ping
                    var endpoint = new IPEndPoint(_localEndpoint.Address, _localEndpoint.Port);

                    _logger.LogInformation("Gossip.Mesh ping {endpoint}", endpoint);

                    var bytes = new byte[512];
                    bytes[0] = 0x01;
                    await udpClient.SendAsync(bytes, 512, endpoint).ConfigureAwait(false);

                    await Task.Delay(_protocolPeriodMs).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Gossip.Mesh threw an unhandled exception");
            }
            finally
            {
                udpClient.Close();
            }
        }

        public async Task StartListener()
        {
            var udpServer = CreateUdpClient();

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
                _logger.LogError(e, "Gossip.Mesh threw an unhandled exception");
            }
            finally
            {
                udpServer.Close();
            }
        }

        public UdpClient CreateUdpClient()
        {
            var udpClient = new UdpClient();
            udpClient.Client.SetSocketOption(
                        SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            udpClient.Client.Bind(_localEndpoint);

            return udpClient;
        }
    }
}