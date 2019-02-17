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
        ConcurrentDictionary<byte, IServiceClientFactory> serviceClientFactories = new ConcurrentDictionary<byte, IServiceClientFactory>();
        ConcurrentDictionary<byte, List<IPEndPoint>> serviceEndPoints = new ConcurrentDictionary<byte, List<IPEndPoint>>();
        ConcurrentDictionary<IPEndPoint, IServiceClient> serviceClients = new ConcurrentDictionary<IPEndPoint, IServiceClient>();

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
                serviceEndPoints.TryAdd(member.Service, endPoints);
            }

            var endPoint = new IPEndPoint(member.IP, member.ServicePort);
            lock (endPoints)
            {
                var containsEndPoint = endPoints.Contains(endPoint);
                if (!containsEndPoint && member.State <= MemberState.Suspicious)
                {
                    endPoints.Add(endPoint);
                }

                else if (containsEndPoint && member.State >= MemberState.Dead)
                {
                    endPoints.Remove(endPoint);
                }
            }
        }

        public T GetServiceClient<T>(byte serviceType) where T : IServiceClient
        {
            if (serviceEndPoints.TryGetValue(serviceType, out var endPoints))
            {
                IPEndPoint endPoint;
                lock (endPoints)
                {
                    endPoint = endPoints[random.Next(0, endPoints.Count())];
                }

                if (!serviceClients.TryGetValue(endPoint, out var serviceClient))
                {
                    if (serviceClientFactories.TryGetValue(serviceType, out var serviceClientFactory))
                    {
                        serviceClient = serviceClientFactory.CreateServiceClient(endPoint);
                        serviceClients.TryAdd(endPoint, serviceClient);
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
            serviceClientFactories.TryAdd(serviceType, serviceClientFactory);
        }
    }
}