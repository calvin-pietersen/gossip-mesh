using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace GossipMesh.LoadBalancing
{
    public class RandomLoadBalancer : ILoadBalancer, IListener
    {
        private readonly Dictionary<byte, IServiceClientFactory> _serviceClientFactories = new Dictionary<byte, IServiceClientFactory>();
        private readonly object _locker = new object();
        private Dictionary<byte, List<IServiceClient>> _serviceToServiceClients = new Dictionary<byte, List<IServiceClient>>();

        private readonly Random _random = new Random();

        public RandomLoadBalancer(Dictionary<byte, IServiceClientFactory> serviceClientFactories)
        {
            _serviceClientFactories = serviceClientFactories;
        }

        public Task Accept(NodeEvent nodeEvent)
        {
            IServiceClientFactory serviceClientFactory;
            if (!_serviceClientFactories.TryGetValue(nodeEvent.Service, out serviceClientFactory))
            {
                return Task.CompletedTask;
            }

            lock (_locker)
            {
                var newServiceToServiceClients = new Dictionary<byte, List<IServiceClient>>(_serviceToServiceClients);

                List<IServiceClient> serviceClients;
                if (!newServiceToServiceClients.TryGetValue(nodeEvent.Service, out serviceClients))
                {
                    serviceClients = new List<IServiceClient>();
                }

                List<IServiceClient> newServiceClients;
                var serviceClient = serviceClients.FirstOrDefault(s => s.ServiceEndPoint.Address.Equals(nodeEvent.IP) && s.ServiceEndPoint.Port == nodeEvent.ServicePort);
                if (serviceClient == null && nodeEvent.State == NodeState.Alive)
                {
                    newServiceClients = new List<IServiceClient>(serviceClients);
                    newServiceClients.Add(serviceClientFactory.CreateServiceClient(new IPEndPoint(nodeEvent.IP, nodeEvent.ServicePort)));
                }

                else if (serviceClient != null && nodeEvent.State >= NodeState.Suspicious)
                {
                    newServiceClients = new List<IServiceClient>(serviceClients);
                    newServiceClients.Remove(serviceClient);
                }

                else
                {
                    return Task.CompletedTask;
                }

                newServiceToServiceClients[nodeEvent.Service] = newServiceClients;
                _serviceToServiceClients = newServiceToServiceClients;

                return Task.CompletedTask;
            }
        }

        public T GetServiceClient<T>(byte serviceType) where T : IServiceClient
        {
            if (_serviceToServiceClients.TryGetValue(serviceType, out var serviceClients) && serviceClients.Any())
            {
                var serviceClient = serviceClients[_random.Next(0, serviceClients.Count)];
                return (T)serviceClient;
            }

            throw new Exception("No service clients available");
        }
    }
}