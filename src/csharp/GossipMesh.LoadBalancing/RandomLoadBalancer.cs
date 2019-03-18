using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace GossipMesh.LoadBalancing
{
    public class RandomLoadBalancer : ILoadBalancer, IMemberListener
    {
        private readonly Dictionary<byte, IServiceClientFactory> _serviceClientFactories = new Dictionary<byte, IServiceClientFactory>();
        private readonly object _locker = new object();
        private Dictionary<byte, List<IServiceClient>> _serviceToServiceClients = new Dictionary<byte, List<IServiceClient>>();

        private readonly Random _random = new Random();

        public RandomLoadBalancer(Dictionary<byte, IServiceClientFactory> serviceClientFactories)
        {
            _serviceClientFactories = serviceClientFactories;
        }

        public Task MemberUpdatedCallback(MemberEvent memberEvent)
        {
            IServiceClientFactory serviceClientFactory;
            if (!_serviceClientFactories.TryGetValue(memberEvent.Service, out serviceClientFactory))
            {
                return Task.CompletedTask;
            }

            lock (_locker)
            {
                var newServiceToServiceClients = new Dictionary<byte, List<IServiceClient>>(_serviceToServiceClients);

                List<IServiceClient> serviceClients;
                if (!newServiceToServiceClients.TryGetValue(memberEvent.Service, out serviceClients))
                {
                    serviceClients = new List<IServiceClient>();
                }

                List<IServiceClient> newServiceClients;
                var serviceClient = serviceClients.FirstOrDefault(s => s.ServiceEndPoint.Address.Equals(memberEvent.IP) && s.ServiceEndPoint.Port == memberEvent.ServicePort);
                if (serviceClient == null && memberEvent.State == MemberState.Alive)
                {
                    newServiceClients = new List<IServiceClient>(serviceClients);
                    newServiceClients.Add(serviceClientFactory.CreateServiceClient(new IPEndPoint(memberEvent.IP, memberEvent.ServicePort)));
                }

                else if (serviceClient != null && memberEvent.State >= MemberState.Suspicious)
                {
                    newServiceClients = new List<IServiceClient>(serviceClients);
                    newServiceClients.Remove(serviceClient);
                }

                else
                {
                    return Task.CompletedTask;
                }

                newServiceToServiceClients[memberEvent.Service] = newServiceClients;
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