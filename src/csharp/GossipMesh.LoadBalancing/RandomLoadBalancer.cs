using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using GossipMesh.Core;

namespace GossipMesh.LoadBalancing
{
    public class RandomLoadBalancer : ILoadBalancer, IMemberListener
    {
        Dictionary<byte, IServiceClientFactory> serviceClientFactories = new Dictionary<byte, IServiceClientFactory>();
        Dictionary<byte, List<IPEndPoint>> serviceEndPoints = new Dictionary<byte, List<IPEndPoint>>();
        Dictionary<IPEndPoint, IServiceClient> serviceClients = new Dictionary<IPEndPoint, IServiceClient>();

        Random random = new Random();

        public RandomLoadBalancer()
        {
        }

        public void MemberCallback(Member member)
        {
            List<IPEndPoint> endPoints;

            if (!serviceEndPoints.TryGetValue(member.Service, out endPoints))
            {
                endPoints = new List<IPEndPoint>();
                serviceEndPoints.Add(member.Service, endPoints);
            }

            var endPoint = new IPEndPoint(member.IP, member.ServicePort); 
            if (!endPoints.Contains(endPoint))
            {
                endPoints.Add(endPoint);
            }
        }

        public T GetServiceClient<T>(byte serviceType) where T : IServiceClient
        {
            if (serviceEndPoints.TryGetValue(serviceType, out var endPoints))
            {
                var endPoint = endPoints[random.Next(0, endPoints.Count())];

                if (!serviceClients.TryGetValue(endPoint, out var serviceClient))
                {
                    if (serviceClientFactories.TryGetValue(serviceType, out var serviceClientFactory))
                    {
                        serviceClient = serviceClientFactory.CreateServiceClient(endPoint);
                        serviceClients.Add(endPoint, serviceClient);
                    }

                    else
                    {
                        throw new Exception("no service client factory registered");
                    }
                }

                return (T)serviceClient;
            }

            throw new Exception("Could not find service endpoint.");
        }

        public void RegisterServiceClientFactory(byte serviceType, IServiceClientFactory serviceClientFactory)
        {
            serviceClientFactories.Add(serviceType, serviceClientFactory);
        }
    }
}