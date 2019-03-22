package helloworld;

import com.rokt.gossip.*;
import io.grpc.Channel;
import io.grpc.ManagedChannel;
import io.grpc.ManagedChannelBuilder;
import io.grpc.stub.AbstractStub;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.Reader;
import java.net.Inet4Address;
import java.util.HashMap;
import java.util.Map;
import java.util.Random;
import java.util.function.Function;

import static com.rokt.gossip.NodeHealth.*;

public class Example {
    static interface ServiceFactory<T> {
        T create(Inet4Address ip, short port);
        void destroy(T service);

        static <T extends AbstractStub<T>> ServiceFactory<T> grpcFactory(Function<Channel, T> make) {
            return new ServiceFactory<T>() {
                @Override
                public T create(Inet4Address ip, short port) {
                    ManagedChannel channel = ManagedChannelBuilder
                            .forAddress(ip.getHostAddress(), port)
                            .usePlaintext()
                            .keepAliveWithoutCalls(true)
                            .build();
                    return make.apply(channel);
                }

                @Override
                public void destroy(T service) {
                    ((ManagedChannel)service.getChannel()).shutdown();
                }
            };
        }
    }

    static class LoadBalancer<T> implements Listener {
        private final Map<Byte, ServiceFactory<T>> serviceFactories;
        private final Map<Byte, Map<NodeAddress, T>> services;
        private final Random random;

        public LoadBalancer(Map<Byte, ServiceFactory<T>> serviceFactories) {
            this.serviceFactories = serviceFactories;
            this.services = new HashMap<>();
            this.random = new Random();
        }

        @Override
        public void accept(NodeAddress from, NodeAddress address, NodeState state, NodeState oldState) {
            if (state == null || state.serviceByte != oldState.serviceByte || state.health == NodeHealth.DEAD || state.health == LEFT) {
                services.computeIfPresent(oldState.serviceByte, (b, nodes) -> {
                    T service = nodes.remove(address);
                    if (service != null) {
                        serviceFactories.get(oldState.serviceByte).destroy(service);
                    }
                    return nodes.isEmpty() ? null : nodes;
                });
            }
            if (state != null && (state.health == ALIVE || state.health == SUSPICIOUS)) {
                services.computeIfAbsent(state.serviceByte, HashMap::new)
                        .put(address, serviceFactories.get(state.serviceByte).create(address.address, address.port));
            }
        }

        public <K extends T> K getEndpoint(byte service) {
            Map<NodeAddress, T> nodes = services.get(service);
            if (nodes == null || nodes.isEmpty()) {
                throw new RuntimeException("No services available to handle request");
            } else {
                Object[] arr = nodes.values().toArray(new Object[0]);
                int index = random.nextInt(arr.length);
                return (K) arr[index];
            }
        }
    }

    public static void main(String[] args) throws Exception {
        LoadBalancer<GreeterGrpc.GreeterBlockingStub> loadBalancer = new LoadBalancer<>(new HashMap<Byte, ServiceFactory<GreeterGrpc.GreeterBlockingStub>>(){{
            this.put((byte) 0x02, ServiceFactory.grpcFactory(GreeterGrpc::newBlockingStub));
        }});

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

                GreeterGrpc.GreeterBlockingStub greeter = loadBalancer.getEndpoint((byte) 0x02);
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
