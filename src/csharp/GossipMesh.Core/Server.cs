using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace GossipMesh.Core
{
    public class Server
    {
        public struct UdpState
        {
            public UdpClient u;
            public IPEndPoint e;
        }

        private int _listenPort;

        public Server(int listenPort)
        {
            _listenPort = listenPort;
        }

        public async Task StartAsync(IEnumerable<string> seeds)
        {
            var listener = new UdpClient(_listenPort);
            var groupEP = new IPEndPoint(IPAddress.Any, _listenPort);

            var state = new UdpState
            {
                u = listener,
                e = groupEP
            };

            try
            {
                while (true)
                {
                    Console.WriteLine("Waiting for broadcast");
                    var request = await listener.ReceiveAsync().ConfigureAwait(false);

                    Console.WriteLine(System.Text.ASCIIEncoding.ASCII.GetString(request.Buffer));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            finally
            {
                listener.Close();
            }
        }
    }
}
