package helloworld;

import com.gossipmesh.core.Listener;
import com.gossipmesh.core.MemberAddress;
import com.gossipmesh.core.Member;

import java.util.HashMap;
import java.util.Map;
import java.util.Random;
import java.util.concurrent.ConcurrentHashMap;

import static com.gossipmesh.core.MemberState.*;

@SuppressWarnings("unchecked")
class LoadBalancer implements Listener {
    private final Map<Byte, Object> serviceFactories;
    private final Map<Byte, Map<MemberAddress, Object>> services;
    private final Random random;

    public LoadBalancer() {
        this.serviceFactories = new HashMap<>();
        this.services = new HashMap<>();
        this.random = new Random();
    }

    public <T> Service<T> registerService(int serviceByte, ServiceFactory<T> factory) {
        assert !serviceFactories.containsKey((byte) serviceByte);
        serviceFactories.put((byte) serviceByte, factory);
        return new Service<>((byte) serviceByte);
    }

    public class Service<T> {
        private byte serviceByte;

        private Service(byte serviceByte) {
            this.serviceByte = serviceByte;
        }

        public T getEndpoint() {
            Map<MemberAddress, ?> nodes = LoadBalancer.this.services.get(serviceByte);
            if (nodes == null || nodes.isEmpty()) {
                throw new RuntimeException("No services available to handle request");
            } else {
                Object[] arr = nodes.values().toArray(new Object[0]);
                int index = LoadBalancer.this.random.nextInt(arr.length);
                return (T) arr[index];
            }
        }
    }

    private static boolean isAlive(Member member) {
        return member != null && (member.state == ALIVE || member.state == SUSPICIOUS);
    }

    private static boolean isDead(Member member) {
        return member == null || member.state == DEAD || member.state == LEFT;
    }

    @Override
    public void accept(MemberAddress from, MemberAddress address, Member member, Member oldMember) {
        boolean serviceUpdated = (member != null && oldMember != null)
                && (member.serviceByte != oldMember.serviceByte)
                && (member.servicePort != oldMember.servicePort);
        if (isAlive(oldMember) && (isDead(member) || serviceUpdated)) {
            services.computeIfPresent(oldMember.serviceByte, (b, nodes) -> {
                Object service = nodes.remove(address);
                if (service != null) {
                    ServiceFactory<Object> factory = (ServiceFactory<Object>) serviceFactories.get(oldMember.serviceByte);
                    factory.destroy(service);
                }
                return nodes.isEmpty() ? null : nodes;
            });
        }
        if ((isDead(oldMember) && isAlive(member)) || serviceUpdated) {
            ServiceFactory<Object> factory = (ServiceFactory<Object>) serviceFactories.get(member.serviceByte);
            if (factory != null) { // we can't build a client if we don't have the service byte registered!
                services.computeIfAbsent(member.serviceByte, ConcurrentHashMap::new)
                        .put(address, factory.create(address.address, member.servicePort));
            }
        }
    }
}
