# Running the C# Aeron examples

You will need to run the Aeron media driver, which will be downloaded into your nuget repository if you build any of the Aeron projects using `dotnet build`.

    java -cp ~/.nuget/packages/aeron.driver/1.15.0/content/driver/media-driver.jar io.aeron.driver.MediaDriver

To run the client run the following in `examples/csharp/Aeron/AeronClient`

    dotnet run <gossiper-port>

Where `gossiper-port` is the port you would like the Gossiper to bind.

Additionally, you pass in seed addresses to bootstap off of.

    dotnet run <gossiper-port> <seed-ip:seed-port> <seed-ip:seed-port>........


To run the server run the following in `examples/csharp/Aeron/AeronServer`

    dotnet run <gossiper-port> <listen-port>

Where `listen-port` is the port Aeron server will bind to.

Additionally, you pass in seed addresses to bootstap off of.

    dotnet run <gossiper-port> <listen-port> <seed-ip:seed-port> <seed-ip:seed-port>........