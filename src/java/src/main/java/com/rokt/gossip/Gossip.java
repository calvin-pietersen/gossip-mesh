package com.rokt.gossip;

import java.io.*;
import java.net.*;
import java.util.*;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.ScheduledThreadPoolExecutor;
import java.util.concurrent.TimeUnit;
import java.util.function.BiPredicate;
import java.util.function.Function;
import java.util.logging.Level;
import java.util.logging.Logger;

public class Gossip {
    private static final Logger LOGGER = Logger.getLogger(Gossip.class.getCanonicalName());
    private static final int PROTOCOL_PERIOD_MS = 1000;
    private static final int PING_TIMEOUT_MS = 200;
    private static final int INDIRECT_PING_TIMEOUT_MS = 400;
    private static final int DEATH_TIMEOUT_MS = 60000;
    private final Map<NodeAddress, NodeState> states;
    private final Map<NodeAddress, ScheduledFuture> waiting;
    private final byte serviceByte;
    private final short servicePort;
    private final ScheduledExecutorService executor;
    private final DatagramSocket socket;
    private final HashMap<Object, Listener> listeners;
    private Thread listener;
    private byte generation;

    public Gossip(int serviceByte, int servicePort) throws IOException {
        this(new DatagramSocket(), serviceByte, servicePort);
    }

    public Gossip(int port, int serviceByte, int servicePort) throws IOException {
        this(new DatagramSocket(port), serviceByte, servicePort);
    }

    public Gossip(DatagramSocket socket, int serviceByte, int servicePort) {
        this.states = new HashMap<>();
        this.waiting = new HashMap<>();
        this.serviceByte = (byte) serviceByte;
        this.servicePort = (short) servicePort;
        this.executor = new ScheduledThreadPoolExecutor(1);
        this.socket = socket;
        this.listeners = new HashMap<>();
    }

    private Runnable loggingExceptions(Runnable f) {
        return () -> {
            try {
                f.run();
            } catch (Exception ex) {
                LOGGER.log(Level.SEVERE, "Exception thrown in task", ex);
            }
        };
    }

    public int start() throws IOException {
        this.executor.scheduleAtFixedRate(loggingExceptions(() -> {
            try {
                probe();
            } catch (IOException ex) {
                LOGGER.log(Level.SEVERE, "Exception thrown when trying to probe", ex);
            }
        }), 0, PROTOCOL_PERIOD_MS, TimeUnit.MILLISECONDS);
        socket.setSoTimeout(500);
        this.listener = new Thread(() -> {
            byte[] buffer = new byte[508];
            while (!Thread.currentThread().isInterrupted()) {
                try {
                    DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                    socket.receive(packet);
                    assert (packet.getOffset() == 0);
                    NodeAddress address = new NodeAddress(
                            (Inet4Address) packet.getAddress(),
                            (short) packet.getPort());
                    byte[] recvBuffer = Arrays.copyOf(packet.getData(), packet.getLength());
                    this.executor.execute(loggingExceptions(() -> {
                        try (InputStream is = new ByteArrayInputStream(recvBuffer);
                             DataInputStream dis = new DataInputStream(is)) {
                            handleMessage(address, dis);
                        } catch (IOException ex) {
                            LOGGER.log(Level.SEVERE, "IO Exception while handling a message from " + address, ex);
                        }
                    }));

                } catch (SocketTimeoutException ex) {
                    // do nothing
                } catch (IOException ex) {
                    if (!Objects.equals(ex.getMessage(), "Socket closed")) {
                        LOGGER.log(Level.SEVERE, "IO Exception reading from datagram socket", ex);
                    }
                }
            }
        });
        this.listener.setDaemon(true);
        this.listener.start();
        return socket.getLocalPort();
    }

    private Iterable<NodeAddress> randomNodes() {
        return randomNodes((address, state) -> true);
    }

    private Iterable<NodeAddress> randomNodes(BiPredicate<NodeAddress, NodeState> matching) {
        return () -> this.states.entrySet()
                .stream()
                .filter(x -> matching.test(x.getKey(), x.getValue()))
                .map(Map.Entry::getKey)
                .sorted(new RandomComparator<>())
                .iterator();
    }

    private class RandomComparator<T> implements Comparator<T> {
        private final Map<T, Integer> map = new IdentityHashMap<>();
        private final Random random;

        RandomComparator() {
            this(new Random());
        }

        RandomComparator(Random random) {
            this.random = random;
        }

        @Override
        public int compare(T left, T right) {
            return Integer.compare(valueOf(left), valueOf(right));
        }

        int valueOf(T obj) {
            synchronized (map) {
                return map.computeIfAbsent(obj, k -> random.nextInt());
            }
        }
    }

    public void stop(long timeunit, TimeUnit unit) throws InterruptedException {
        this.listener.interrupt(); // this thread should shut itself down within half a second at worst
        this.socket.close();
        this.executor.shutdownNow();
        this.executor.awaitTermination(timeunit, unit);
    }

    private void probe() throws IOException {
        int i = 3;
        for (NodeAddress address : randomNodes()) {
            if (--i < 0) {
                break;
            }
            LOGGER.log(Level.FINEST, "Performing direct ping on: " + address);
            ping(address);
        }
    }

    private static NodeAddress parseAddress(DataInput stream) throws IOException {
        byte[] addressBytes = new byte[4];
        stream.readFully(addressBytes);
        return new NodeAddress(
                (Inet4Address) Inet4Address.getByAddress(addressBytes),
                stream.readShort());
    }

    private static void writeAddress(DataOutput output, NodeAddress address) throws IOException {
        output.write(address.address.getAddress());
        output.writeShort(address.port);
    }

    private static void writeEvent(DataOutput output, NodeAddress address, NodeState state) throws IOException {
        writeAddress(output, address);
        output.write(state.health.ordinal());
        output.write(state.generation);
        if (state.health == NodeHealth.ALIVE) {
            output.write(state.serviceByte);
            output.writeShort(state.servicePort);
        }
    }

    private void scheduleTask(NodeAddress address, Runnable command, int delay, TimeUnit units) {
        this.waiting.computeIfAbsent(address, a -> {
            NodeState state = states.get(address);
            ScheduledFuture<?>[] future = new ScheduledFuture<?>[1];
            future[0] = this.executor.schedule(loggingExceptions(() -> {
                this.waiting.remove(address, future[0]);
                command.run();
            }), delay, TimeUnit.MILLISECONDS);
            return future[0];
        });
    }

    private void sendMessage(NodeAddress address, MessageWriter<DataOutput> header) throws IOException {
        byte[] outBuffer = new byte[508];
        int limit = 0;
        Set<NodeAddress> sending = new HashSet<>();
        try (FiniteByteArrayOutputStream os = new FiniteByteArrayOutputStream(outBuffer);
             DataOutputStream dos = new DataOutputStream(os)) {
            dos.write(0); // protocol version
            header.writeTo(dos);

            dos.write(this.generation);
            dos.write(this.serviceByte);
            dos.writeShort(this.servicePort);

            NodeState receiver = this.states.get(address);
            if (receiver == null) {
                dos.write(NodeHealth.DEAD.ordinal());
                dos.write(0);
            } else {
                dos.write(receiver.health.ordinal());
                dos.write(receiver.generation);
            }

            limit = os.position();

            PriorityQueue<Map.Entry<NodeAddress, NodeState>> queue = new PriorityQueue<>(Comparator.comparingLong(a -> a.getValue().timesMentioned));
            queue.addAll(this.states.entrySet());
            for (Map.Entry<NodeAddress, NodeState> entry : queue) {
                if (Objects.equals(entry.getKey(), address)) {
                    continue;
                }
                writeEvent(dos, entry.getKey(), entry.getValue()); // this will eventually throw an exception
                limit = os.position();
                sending.add(entry.getKey());
            }
        } catch (IOException ex) {
            // ignore this, we don't actually care
        }

        DatagramPacket packet = new DatagramPacket(outBuffer, limit);
        packet.setAddress(address.address);
        packet.setPort(address.port & 0xFFFF);
        socket.send(packet);

        for (NodeAddress sent : sending) {
            this.states.get(sent).timesMentioned++;
        }
    }

    public void connectTo(Inet4Address address, int port) {
        this.executor.execute(loggingExceptions(() -> {
            try {
                this.ping(new NodeAddress(address, (short) port));
            } catch (IOException ex) {
                LOGGER.log(Level.SEVERE, "Exception thrown while connecting to " + address, ex);
            }
        }));
    }

    private void ping(NodeAddress address) throws IOException {
        sendMessage(address, dos -> dos.write(0x01));
        NodeState state = updateState(null, address, s -> s == null ? new NodeState(NodeHealth.DEAD, (byte) 0, (byte) 0, (byte) 0) : s);
        if (state != null) {
            scheduleTask(address, () -> {
                try {
                    indirectPing(address, state);
                } catch (IOException ex) {
                    LOGGER.log(Level.SEVERE, "Exception thrown while performing indirect ping of " + address, ex);
                }
            }, PING_TIMEOUT_MS, TimeUnit.MILLISECONDS);
        }
    }

    private void indirectPing(NodeAddress address, NodeState state) throws IOException {
        updateState(null, address, s -> s == null ? null : s.merge(state.withHealth(NodeHealth.SUSPICIOUS)));
        int i = 3;
        for (NodeAddress relay : randomNodes((a, s) -> !Objects.equals(a, address))) {
            if (--i < 0) {
                break;
            }
            sendMessage(relay, dos -> {
                dos.write(0x03);
                writeAddress(dos, address);
            });
        }
        scheduleTask(address, () -> {
            updateState(null, address, s -> s == null ? null : s.merge(state.withHealth(NodeHealth.DEAD)));
            scheduleTask(address, () -> {
                updateState(null, address, s -> null);
            }, DEATH_TIMEOUT_MS, TimeUnit.MILLISECONDS);
        }, INDIRECT_PING_TIMEOUT_MS, TimeUnit.MILLISECONDS);
    }

    private void handleMessage(NodeAddress address, DataInput input) throws IOException {
        byte version = input.readByte();
        if (version == 0) {
            byte b = input.readByte();
            switch (b) {
                case 0x00: // direct ack
                    handleDirectAck(address, input);
                    break;
                case 0x01: // direct ping
                    handleDirectPing(address, input);
                    break;
                case 0x04: // ack request
                case 0x05: // ping request
                    handleRequest(address, input, (byte) (b & 0x01));
                    break;
                case 0x06: // forwarded ack
                case 0x07: // forwarded ping
                    handleForwarded(address, input, (byte) (b & 0x01));
                    break;
            }
        } else {
            LOGGER.log(Level.SEVERE, "Unknown protocol version received: " + version);
        }
    }

    private void removeAndCancel(NodeAddress address) {
        ScheduledFuture<?> f = this.waiting.remove(address);
        if (f != null) {
            f.cancel(true);
        }
    }

    private void handleDirectAck(NodeAddress address, DataInput input) throws IOException {
        removeAndCancel(address); // if we were waiting to hear from them - here they are!
        handleEvents(address, input);
    }

    private void handleDirectPing(NodeAddress address, DataInput input) throws IOException {
        removeAndCancel(address); // if we were waiting to hear from them - here they are!
        handleEvents(address, input);
        sendMessage(address, dos -> dos.write(0x00));
    }

    private void handleRequest(NodeAddress address, DataInput input, byte b) throws IOException {
        removeAndCancel(address); // if we were waiting to hear from them - here they are!
        NodeAddress destination = parseAddress(input);
        handleEvents(address, input);

        sendMessage(address, dos -> {
            dos.write(b | 0x06);
            writeAddress(dos, destination);
        });
    }

    private void handleForwarded(NodeAddress address, DataInput input, byte b) throws IOException {
        removeAndCancel(address); // if we were waiting to hear from them - here they are!
        NodeAddress source = parseAddress(input);
        handleEvents(address, input);

        switch (b) {
            case 0x00:
                removeAndCancel(source);
                break;
            case 0x01:
                sendMessage(address, dos -> {
                    dos.write(0x04);
                    writeAddress(dos, source);
                });
                break;
        }
    }

    private NodeState updateState(NodeAddress from, NodeAddress address, Function<NodeState, NodeState> update) {
        NodeState oldState = this.states.get(address);
        NodeState newState = update.apply(oldState);
        if (!Objects.equals(oldState, newState)) {
            if (newState == null) {
                this.states.remove(address);
            } else {
                this.states.put(address, newState);
            }
            notifyListeners(from, address, newState, oldState);
        }
        return newState;
    }

    private void handleEvents(NodeAddress from, DataInput input) {
        try {
            byte senderGeneration = input.readByte();
            byte senderService = input.readByte();
            short senderServicePort = input.readShort();
            NodeState senderState = new NodeState(NodeHealth.ALIVE,
                    senderGeneration,
                    senderService,
                    senderServicePort);
            updateState(null, from, s -> s == null ? senderState : s.merge(senderState));

            NodeHealth myHealth = NodeHealth.values()[input.readByte()];
            byte myGeneration = input.readByte();
            if (myHealth == NodeHealth.SUSPICIOUS || myHealth == NodeHealth.DEAD) {
                byte newGeneration = (byte) (myGeneration + 1);
                if (NodeState.isLaterGeneration(generation, newGeneration)) {
                    newGeneration = generation;
                }
                this.generation = newGeneration;
            }

            // This is an infinte loop that will be broken when we hit an exception,
            // because DataInput doesn't let us see whether we're at the end
            //noinspection InfiniteLoopStatement
            while (true) {
                NodeAddress address = parseAddress(input);
                NodeHealth health = NodeHealth.values()[input.readByte()];
                byte generation = input.readByte();
                byte serviceByte = (health == NodeHealth.ALIVE ? input.readByte() : 0);
                short servicePort = (health == NodeHealth.ALIVE ? input.readShort() : 0);
                NodeState newState = new NodeState(health, generation, serviceByte, servicePort);

                updateState(from, address, s -> {
                    if (s != null) {
                        return s.merge(newState);
                    } else if (health == NodeHealth.ALIVE || health == NodeHealth.SUSPICIOUS) {
                        return newState;
                    } else {
                        // if we don't already know about this node and we get a DEAD or a LEFT: we don't care.
                        // Just ignore it. This means that our pruning will actually remove nodes, because we
                        // won't keep broadcasting dead nodes indefinitely.
                        return null;
                    }
                });
            }
        } catch (IOException ex) {
            // this is fine - just means we're done with handling events
        }
    }

    private void notifyListeners(NodeAddress from, NodeAddress address, NodeState newState, NodeState oldState) {
        for (Listener listener : listeners.values()) {
            listener.accept(from, address, newState, oldState);
        }
    }

    public void addListener(Object key, Listener listener) {
        this.executor.execute(loggingExceptions(() -> this.listeners.put(key, listener)));
    }

    public void removeListener(Object key) {
        this.executor.execute(loggingExceptions(() -> this.listeners.remove(key)));
    }
}
