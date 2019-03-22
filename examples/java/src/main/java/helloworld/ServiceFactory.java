package helloworld;

import io.grpc.Channel;
import io.grpc.ManagedChannel;
import io.grpc.ManagedChannelBuilder;
import io.grpc.stub.AbstractStub;

import java.net.Inet4Address;
import java.util.function.Function;

interface ServiceFactory<T> {
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
