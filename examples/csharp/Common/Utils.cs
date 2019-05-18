using System;
using System.Net;
using Microsoft.Extensions.Logging;

namespace GossipMesh.Examples.Common
{
    public static class Utils
    {
        public static IPEndPoint IPEndPointFromString(string ipEndPointString)
        {
            var endpoint = ipEndPointString.Split(':');
            return new IPEndPoint(IPAddress.Parse(endpoint[0]), int.Parse(endpoint[1]));
        }

        public static ILogger CreateLogger<T>()
        {
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new ConsoleLoggerProvider());
            return loggerFactory
                .CreateLogger<T>();
        }
    }
}