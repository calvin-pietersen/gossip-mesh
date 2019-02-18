using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;
using System.Collections.Concurrent;

namespace GossipMesh.LoadBalancing
{
    public class RandomLoadBalancer : ILoadBalancer, IMemberListener
    {
        private readonly Dictionary<byte, IServiceClientFactory> _serviceClientFactories = new Dictionary<byte, IServiceClientFactory>();
        private readonly object _serviceToServiceClientsLocker = new object();
        Dictionary<byte, List<IServiceClient>> _serviceToServiceClients = new Dictionary<byte, List<IServiceClient>>();

        Random random = new Random();

        public RandomLoadBalancer(Dictionary<byte, IServiceClientFactory> serviceClientFactories)
        {
            _serviceClientFactories = serviceClientFactories;
        }

        public void MemberCallback(Member member)
        {
            IServiceClientFactory serviceClientFactory;
            if (!_serviceClientFactories.TryGetValue(member.Service, out serviceClientFactory))
            {
                return;
            }

            lock (_serviceToServiceClientsLocker)
            {
                var newServiceToServiceClients = new Dictionary<byte, List<IServiceClient>>(_serviceToServiceClients);

                List<IServiceClient> serviceClients;
                if (!newServiceToServiceClients.TryGetValue(member.Service, out serviceClients))
                {
                    serviceClients = new List<IServiceClient>();
                }

                List<IServiceClient> newServiceClients;
                var serviceClient = serviceClients.FirstOrDefault(s => s.ServiceEndPoint.Address.Equals(member.IP) && s.ServiceEndPoint.Port == member.ServicePort);
                if (serviceClient == null && member.State <= MemberState.Suspicious)
                {
                    newServiceClients = new List<IServiceClient>(serviceClients);
                    newServiceClients.Add(serviceClientFactory.CreateServiceClient(new IPEndPoint(member.IP, member.ServicePort)));
                }

                else if (serviceClient != null && member.State >= MemberState.Dead)
                {
                    newServiceClients = new List<IServiceClient>(serviceClients);
                    newServiceClients.Remove(serviceClient);
                }

                else
                {
                    return;
                }

                newServiceToServiceClients[member.Service] = newServiceClients;
                _serviceToServiceClients = newServiceToServiceClients;
            }
        }

        public T GetServiceClient<T>(byte serviceType) where T : IServiceClient
        {
            if (_serviceToServiceClients.TryGetValue(serviceType, out var serviceClients) && serviceClients.Any())
            {
                var serviceClient = serviceClients[random.Next(0, serviceClients.Count)];
                return (T)serviceClient;
            }

            throw new Exception("No service clients available");
        }
    }
}