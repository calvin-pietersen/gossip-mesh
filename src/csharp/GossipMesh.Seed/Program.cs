using System;
using System.Threading.Tasks;
using GossipMesh.Core;

namespace GossipMesh.Seed
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var server = new GossipMesh.Core.Server(11000);
            server.StartAsync();

            while (true)
            {
                Console.WriteLine("seed node doing random things");
                await Task.Delay(100);
            }
        }
    }
}
