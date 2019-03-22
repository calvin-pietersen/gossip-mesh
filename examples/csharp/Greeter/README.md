# Running the C# examples

To run the client or the server run the following in either `examples/csharp/Greeter/GreeterClient` or `examples/csharp/Greeter/GreeterServer`.

    dotnet run <listen-port>

Where `listen-port` is the port you would like the Gossiper/GreeterServer to bind.

Additionally, you pass in seed addresses to bootstap off of.

    dotnet run <listen-port> <seed-ip:seed-port> <seed-ip:seed-port>........
