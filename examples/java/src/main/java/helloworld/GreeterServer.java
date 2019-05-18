package helloworld;

import com.rokt.gossip.Gossip;
import io.grpc.Server;
import io.grpc.ServerBuilder;
import io.grpc.stub.StreamObserver;

import java.net.Inet4Address;
import java.util.concurrent.TimeUnit;

public class GreeterServer extends GreeterGrpc.GreeterImplBase {
    @Override
    public void sayHello(Helloworld.HelloRequest request, StreamObserver<Helloworld.HelloReply> responseObserver) {
        System.out.println("Request: " + request.getName());
        responseObserver.onNext(Helloworld.HelloReply.newBuilder()
                .setMessage("Hello there, " + request.getName())
                .build());
        responseObserver.onCompleted();
    }

    public static void main(String[] args) throws Exception {
        int serverPort = Integer.parseInt(args[0]);
        Server server = ServerBuilder.forPort(serverPort)
                .addService(new GreeterServer())
                .build();
        server.start();

        Gossip gossip = new Gossip(0x02, serverPort);
        int gossipPort = gossip.start();
        System.out.println(gossipPort);
        for (int i = 1; i < args.length; ++i) {
            String[] details = args[i].split(":");
            if (details.length == 1) {
                details = new String[]{"127.0.0.1", details[0]};
            }
            gossip.connectTo(
                    (Inet4Address) Inet4Address.getByName(details[0]),
                    Integer.parseInt(details[1]));
        }

        server.awaitTermination();
        gossip.stop(1, TimeUnit.SECONDS);
    }
}
