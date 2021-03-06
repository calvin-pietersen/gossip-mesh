using System.Threading.Tasks;
using Grpc.Core;
using Helloworld;
using Microsoft.Extensions.Logging;

namespace GreeterServer
{
    class GreeterImpl : Helloworld.Greeter.GreeterBase
    {
        private readonly ILogger _logger;
        public GreeterImpl(ILogger logger)
        {
            _logger = logger;
        }

        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Request: {name}", request.Name);
            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
        }
    }
}