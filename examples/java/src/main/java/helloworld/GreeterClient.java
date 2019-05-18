package helloworld;

import com.rokt.gossip.Gossiper;
import io.grpc.ManagedChannel;
import io.grpc.ManagedChannelBuilder;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.Reader;
import java.net.Inet4Address;

public class GreeterClient {
    private final ManagedChannel channel;
    private final GreeterGrpc.GreeterBlockingStub stub;

    public GreeterClient(ManagedChannel channel) {
        this.channel = channel;
        this.stub = GreeterGrpc.newBlockingStub(channel);
    }

    public String sayHello(String name) {
        Helloworld.HelloReply response = stub.sayHello(Helloworld.HelloRequest.newBuilder()
                .setName(name)
                .build());
        return response.getMessage();
    }

    public String authority() {
        return channel.authority();
    }

    static ServiceFactory<GreeterClient> factory = new ServiceFactory<GreeterClient>() {
        @Override
        public GreeterClient create(Inet4Address ip, short port) {
            ManagedChannel channel = ManagedChannelBuilder
                    .forAddress(ip.getHostAddress(), port)
                    .usePlaintext()
                    .keepAliveWithoutCalls(true)
                    .build();
            return new GreeterClient(channel);
        }

        @Override
        public void destroy(GreeterClient service) {
            service.channel.shutdown();
        }
    };

    public static void main(String[] args) throws Exception {
        LoadBalancer loadBalancer = new LoadBalancer();
        LoadBalancer.Service<GreeterClient> greeterService =
                loadBalancer.registerService(0x02, GreeterClient.factory);

        Gossiper gossiper = new Gossiper(0, 0);
        gossiper.addListener("load-balancer", loadBalancer);
        int gossipPort = gossiper.start();
        System.out.println(gossipPort);
        for (int i = 0; i < args.length; ++i) {
            String[] details = args[i].split(":");
            if (details.length == 1) {
                details = new String[]{"127.0.0.1", details[0]};
            }
            gossiper.connectTo(
                    (Inet4Address) Inet4Address.getByName(details[0]),
                    Integer.parseInt(details[1]));
        }

        try (Reader reader = new InputStreamReader(System.in);
             BufferedReader bufferedReader = new BufferedReader(reader)) {
            while (true) {
                System.out.print("Enter your name: ");
                System.out.flush();
                String name = bufferedReader.readLine();

                GreeterClient greeter = greeterService.getEndpoint();
                long start = System.nanoTime();
                String response = greeter.sayHello(name);
                long end = System.nanoTime();
                double time = ((double) (end - start)) / 1000000;
                System.out.println(greeter.authority() + " " + response + " took " + time + "ms");
            }
        }
    }
}
