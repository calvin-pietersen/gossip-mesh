# Running the C# ZeroMQ examples

To run the client or the server run the following in either `examples/csharp/Grpc/ZeroMQ/ZeroMQClient` or `examples/csharp/Grpc/ZeroMQ/ZeroMQServer`.

    dotnet run <listen-port>

Where `listen-port` is the port you would like the Gossiper/ZeroMQServer to bind.

Additionally, you pass in seed addresses to bootstap off of.

    dotnet run <listen-port> <seed-ip:seed-port> <seed-ip:seed-port>........
