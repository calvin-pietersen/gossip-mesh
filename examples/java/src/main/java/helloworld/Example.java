package helloworld;

import com.rokt.gossip.*;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.Reader;
import java.net.Inet4Address;

public class Example {

    public static void main(String[] args) throws Exception {
        LoadBalancer loadBalancer = new LoadBalancer();
        LoadBalancer.Service<GreeterGrpc.GreeterBlockingStub> greeterService = loadBalancer.registerService((byte) 0x02, ServiceFactory.grpcFactory(GreeterGrpc::newBlockingStub));

        Gossip gossip = new Gossip((byte) 100, (short) 0);
        gossip.addListener("load-balancer", loadBalancer);
        gossip.start();
        gossip.connectTo((Inet4Address) Inet4Address.getByName("127.0.0.1"), (short) 10000);

        try (Reader reader = new InputStreamReader(System.in);
             BufferedReader bufferedReader = new BufferedReader(reader)) {
            while (true) {
                System.out.print("Enter your name: ");
                System.out.flush();
                String name = bufferedReader.readLine();

                GreeterGrpc.GreeterBlockingStub greeter = greeterService.getEndpoint();
                long start = System.nanoTime();
                Helloworld.HelloReply response = greeter.sayHello(Helloworld.HelloRequest.newBuilder()
                        .setName(name)
                        .build());
                long end = System.nanoTime();
                double time = ((double) (end - start)) / 1000000;
                System.out.println(greeter.getChannel().authority() + " " + response.getMessage() + " took " + time + "ms");
            }
        }
    }
}
