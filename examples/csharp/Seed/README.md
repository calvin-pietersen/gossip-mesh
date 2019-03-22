# Running the Seed UI App

The seed UI App allows you to monitor the state of the cluster via a web UI. To start the seed app run the following from `examples/cshar/seed/`

    dotnet run --port <listen-port>

Where `listen-port` is the port you would like the Gossiper to bind.

Additionally, you pass in seed addresses to bootstap off of.

    dotnet run --port <listen-port> --seeds <seed-ip:seed-port>,<seed-ip:seed-port>........