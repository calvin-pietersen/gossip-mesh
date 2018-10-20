using System;
using System.Collections.Generic;
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
        private readonly List<IPEndPoint> _seedMembers;

        private readonly ILogger _logger;

        public Server(int listenPort, int protocolPeriodMs, ILogger logger, List<IPEndPoint> seedMembers = null)
        {
            _localEndpoint = new IPEndPoint(IPAddress.Any, listenPort);
            _protocolPeriodMs = protocolPeriodMs;
            _seedMembers = seedMembers ?? new List<IPEndPoint>();

            _logger = logger;
        }

        public void Start()
        {
            _logger.LogInformation("Starting Gossip.Mesh server");
            // recieve
            Task.Run(async () => await StartListener().ConfigureAwait(false)).ConfigureAwait(false);

            // ping
            Task.Run(async () => await GossipPump().ConfigureAwait(false)).ConfigureAwait(false);
        }

        public async Task GossipPump()
        {
            var udpClient = CreateUdpClient();

            try
            {
                while (true)
                {
                    // ping member

                    // ping seed
                    if (_seedMembers.Count > 0)
                    {
                        Random rand = new Random();
                        var i = rand.Next(0, _seedMembers.Count);
                        await PingAsync(udpClient, _seedMembers[i]);
                    }

                    await Task.Delay(_protocolPeriodMs).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception");
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception");
            }
            finally
            {
                udpServer.Close();
            }
        }

        public UdpClient CreateUdpClient()
        {
            var udpClient = new UdpClient();
            try
            {
                udpClient.Client.SetSocketOption(
                            SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                udpClient.Client.Bind(_localEndpoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception");
            }

            return udpClient;
        }

        public async Task PingAsync(UdpClient udpClient, IPEndPoint endpoint)
        {
            _logger.LogInformation("Gossip.Mesh ping {endpoint}", endpoint);

            var bytes = new byte[512];
            bytes[0] = 0x01;
            await udpClient.SendAsync(bytes, 512, endpoint).ConfigureAwait(false);
        }
    }
}