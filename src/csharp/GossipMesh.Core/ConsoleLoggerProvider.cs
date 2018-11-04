using System;
using Microsoft.Extensions.Logging;
namespace GossipMesh.Core
{
    public class ConsoleLoggerProvider : ILoggerProvider
    {
        public void Dispose() { }

        public ILogger CreateLogger(string categoryName)
        {
            return new ConsoleLogger(categoryName);
        }

        public class ConsoleLogger : ILogger
        {
            private readonly string _categoryName;

            public ConsoleLogger(string categoryName)
            {
                _categoryName = categoryName;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")} {logLevel}: {formatter(state, exception)}");
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                if (logLevel == LogLevel.Debug) return false;

                return true;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }
        }
    }
}