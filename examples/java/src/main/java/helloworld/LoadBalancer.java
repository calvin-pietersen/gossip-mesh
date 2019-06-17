package helloworld;

import com.gossipmesh.gossip.Listener;
import com.gossipmesh.gossip.MemberAddress;
import com.gossipmesh.gossip.Member;

import java.util.HashMap;
import java.util.Map;
import java.util.Random;
import java.util.concurrent.ConcurrentHashMap;

import static com.gossipmesh.gossip.MemberState.*;

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

    private static boolean isAlive(Member state) {
        return state != null && (state.health == ALIVE || state.health == SUSPICIOUS);
    }

    private static boolean isDead(Member state) {
        return state == null || state.health == DEAD || state.health == LEFT;
    }

    @Override
    public void accept(MemberAddress from, MemberAddress address, Member state, Member oldState) {
        boolean serviceUpdated = (state != null && oldState != null)
                && (state.serviceByte != oldState.serviceByte)
                && (state.servicePort != oldState.servicePort);
        if (isAlive(oldState) && (isDead(state) || serviceUpdated)) {
            services.computeIfPresent(oldState.serviceByte, (b, nodes) -> {
                Object service = nodes.remove(address);
                if (service != null) {
                    ServiceFactory<Object> factory = (ServiceFactory<Object>) serviceFactories.get(oldState.serviceByte);
                    factory.destroy(service);
                }
                return nodes.isEmpty() ? null : nodes;
            });
        }
        if ((isDead(oldState) && isAlive(state)) || serviceUpdated) {
            ServiceFactory<Object> factory = (ServiceFactory<Object>) serviceFactories.get(state.serviceByte);
            if (factory != null) { // we can't build a client if we don't have the service byte registered!
                services.computeIfAbsent(state.serviceByte, ConcurrentHashMap::new)
                        .put(address, factory.create(address.address, state.servicePort));
            }
        }
    }
}
