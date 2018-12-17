package com.rokt.gossip;

import java.io.*;
import java.net.*;
import java.util.*;

public class Gossip {
    private static final long PING_TIMEOUT_TICKS = 1;
    private static final long SUSPICIOUS_TIMEOUT_TICKS = 2;
    private static final long DEAD_TIMEOUT_TICKS = 100;
    private static final int PROTOCOL_PERIOD_MS = 1000;
    private final Map<NodeAddress, NodeState> states;
    private final Map<NodeAddress, Long> waiting;
    private final NodeAddress address;
    private DatagramSocket socket;
    private Thread listenerThread;
    private Thread tickingThread;
    private long currentTick;

    public Gossip(Inet4Address address, short port, byte serviceByte, short servicePort) {
        this.address = new NodeAddress(address, port);
        this.states = new HashMap<>();
        this.waiting = new HashMap<>();
        this.listenerThread = null;
        this.tickingThread = null;
        this.states.put(this.address, new NodeState(NodeHealth.ALIVE, (byte) 0, serviceByte, servicePort));
    }

    public void start() throws IOException {
        final Object lock = new Object();
        this.socket = new DatagramSocket(address.port, address.address);
        socket.setSoTimeout(500);
        this.listenerThread = new Thread(() -> {
            byte[] buffer = new byte[508];
            while (!Thread.currentThread().isInterrupted()) {
                try {
                    DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                    socket.receive(packet);
                    assert (packet.getOffset() == 0);
                    byte[] recvBuffer = Arrays.copyOf(packet.getData(), packet.getLength());
                    synchronized (lock) {
                        try (InputStream is = new ByteArrayInputStream(recvBuffer);
                             DataInputStream dis = new DataInputStream(is)) {
                            handleMessage(
                                    new NodeAddress(
                                            (Inet4Address) packet.getAddress(),
                                            (short) packet.getPort()),
                                    dis);
                        }
                    }
                } catch (SocketTimeoutException ex) {
                    // do nothing
                } catch (IOException ex) {
                    if (!Objects.equals(ex.getMessage(), "Socket closed")) {
                        ex.printStackTrace();
                    }
                }
            }
        });
        this.listenerThread.start();
        this.tickingThread = new Thread(() -> {
            while (!Thread.currentThread().isInterrupted()) {
                try {
                    synchronized (lock) {
                        handleTick();
                    }
                    Thread.sleep(PROTOCOL_PERIOD_MS);
                } catch (IOException ex) {
                    if (!Objects.equals(ex.getMessage(), "Socket closed")) {
                        ex.printStackTrace();
                    }
                } catch (InterruptedException ex) {
                    Thread.currentThread().interrupt();
                }
            }
        });
        this.tickingThread.start();
        System.out.println("Started on " + this.address);
    }

    public void stop() throws InterruptedException {
        if (this.tickingThread != null) {
            this.tickingThread.interrupt();
            this.tickingThread.join();
            this.tickingThread = null;
        }
        if (this.listenerThread != null) {
            this.listenerThread.interrupt();
            this.socket.close();
            this.listenerThread.join();
            this.listenerThread = null;
        }
    }

    private void handleMessage(NodeAddress address, DataInput input) throws IOException {
        this.waiting.remove(address); // if we were waiting to hear from them - here they are!
        switch (input.readByte()) {
            case 0: // direct ack
                handleDirectAck(address, input);
                break;
            case 1: // direct ping
                handleDirectPing(address, input);
                break;
            case 2: // indirect ack
                handleIndirectAck(address, input);
                break;
            case 3: // indirect ping
                handleIndirectPing(address, input);
                break;
        }
    }

    private static final Object o = new Object();
    private void handleTick() throws IOException {
        synchronized (o) {
            this.currentTick++;
            expire();
            probe();
            if (this.address.port == 5001) {
                System.out.println("Tick " + this.currentTick);
                System.out.println(states);
                System.out.println(waiting);
                System.out.println();
            }
        }
    }

    private void expire() throws IOException {
        Iterator<Map.Entry<NodeAddress, Long>> iterator = this.waiting.entrySet().iterator();
        while (iterator.hasNext()) {
            Map.Entry<NodeAddress, Long> entry = iterator.next();
            if (entry.getValue() <= this.currentTick) {
                NodeState newState = this.states.computeIfPresent(entry.getKey(), (address, state) -> {
                    switch (state.health) {
                        case ALIVE: // become suspicious
                            return new NodeState(NodeHealth.SUSPICIOUS, state.generation, state.serviceByte, state.servicePort);
                        case SUSPICIOUS: // become dead
                            return new NodeState(NodeHealth.DEAD, state.generation, state.serviceByte, state.servicePort);
                        default:
                            return null;
                    }
                });
                System.out.println(entry.getKey() + " => " + newState);
                if (newState != null) {
                    switch (newState.health) {
                        case SUSPICIOUS:
                            this.indirectPing(entry.getKey());
                            entry.setValue(this.currentTick + SUSPICIOUS_TIMEOUT_TICKS);
                            break;
                        case DEAD:
                            entry.setValue(this.currentTick + DEAD_TIMEOUT_TICKS);
                            break;
                        default:
                            iterator.remove();
                    }
                }
            }
        }
    }

    private void indirectPing(NodeAddress address) throws IOException {
        // pick N nodes to use to indirectly ping address
        int N = 3;
        // pick M nodes to ping
        List<NodeAddress> addresses = new ArrayList<>(this.states.keySet());
        Collections.shuffle(addresses);
        for (int i = 0; i < N && i < addresses.size(); ++i) {
            NodeAddress addr = addresses.get(i);
            if (!Objects.equals(
                    this.address,
                    addr)) {
                sendIndirectPing(address, addr);
            }
        }
    }

    private void probe() throws IOException {
        int M = 3;
        // pick M nodes to ping
        List<NodeAddress> addresses = new ArrayList<>(this.states.keySet());
        Collections.shuffle(addresses);
        for (int i = 0; i < M && i < addresses.size(); ++i) {
            NodeAddress addr = addresses.get(i);
            if (!Objects.equals(
                    this.address,
                    addr)) {
                sendPing(addr);
            }
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
        output.write(state.health.ordinal());
        output.write(address.address.getAddress());
        output.writeShort(address.port);
        output.write(state.generation);
        if (state.health == NodeHealth.ALIVE) {
            output.write(state.serviceByte);
            output.writeShort(state.servicePort);
        }
    }

    private void sendMessage(NodeAddress address, MessageWriter<DataOutput> header) throws IOException {
        byte[] outBuffer = new byte[508];
        int limit = 0;
        Set<NodeAddress> sending = new HashSet<>();
        try (FiniteByteArrayOutputStream os = new FiniteByteArrayOutputStream(outBuffer);
             DataOutputStream dos = new DataOutputStream(os)) {
            header.writeTo(dos);
            limit = os.position();
            PriorityQueue<Map.Entry<NodeAddress, NodeState>> queue = new PriorityQueue<>(Comparator.comparingLong(a -> a.getValue().timesMentioned));
            queue.addAll(this.states.entrySet());
            System.out.println(outBuffer[0]);
            for (Map.Entry<NodeAddress, NodeState> entry : queue) {
                writeEvent(dos, entry.getKey(), entry.getValue()); // this will eventually throw an exception
                limit = os.position();
                sending.add(entry.getKey());
                System.out.println(entry.getKey() + " is in state " + entry.getValue());
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

    public void sendPing(NodeAddress address) throws IOException {
        sendMessage(address, dos -> dos.write(1));
        NodeState state = this.states.putIfAbsent(address, new NodeState(NodeHealth.DEAD, (byte) 0, null, null));
        if (state == null) {
            // if we didn't already know about it, let's give it some time...
            this.waiting.putIfAbsent(address, currentTick + 100);
        } else {
            // if we already knew about it, then give it less time
            this.waiting.putIfAbsent(address, currentTick + PING_TIMEOUT_TICKS);
        }
    }

    private void sendIndirectPing(NodeAddress address, NodeAddress relay) throws IOException {
        sendMessage(relay, dos -> {
            dos.write(3);
            writeAddress(dos, address);
            writeAddress(dos, this.address);
        });
    }

    private void handleDirectAck(NodeAddress address, DataInput input) throws IOException {
        handleEvents(input);
    }

    private void handleDirectPing(NodeAddress address, DataInput input) throws IOException {
        handleEvents(input);
        sendMessage(address, dos -> dos.write(0));
    }

    private void handleIndirectAck(NodeAddress address, DataInput input) throws IOException {
        NodeAddress destination = parseAddress(input);
        NodeAddress source = parseAddress(input);
        handleEvents(input);

        sendMessage(destination, dos -> {
            dos.writeByte(2);
            writeAddress(dos, destination);
            writeAddress(dos, source);
        });
    }

    private void handleIndirectPing(NodeAddress address, DataInput input) throws IOException {
        NodeAddress destination = parseAddress(input);
        NodeAddress source = parseAddress(input);
        handleEvents(input);

        if (Objects.equals(this.address, destination)) {
            sendMessage(address, dos -> {
                dos.write(2);
                writeAddress(dos, source);
                writeAddress(dos, destination);
            });
        } else {
            sendMessage(address, dos -> {
                dos.write(3);
                writeAddress(dos, destination);
                writeAddress(dos, source);
            });
        }
    }

    private void handleEvents(DataInput input) throws IOException {
        try {
            // This is an infinte loop that will be broken when we hit an exception,
            // because DataInput doesn't let us see whether we're at the end
            //noinspection InfiniteLoopStatement
            while (true) {
                NodeHealth health = NodeHealth.values()[input.readByte()];
                NodeAddress address = parseAddress(input);
                byte generation = input.readByte();

                NodeState state;
                NodeState oldState = this.states.get(address);
                if (health == NodeHealth.ALIVE) {
                    byte serviceByte = input.readByte();
                    short servicePort = input.readShort();
                    state = new NodeState(health, generation, serviceByte, servicePort);
                } else {
                    if (Objects.equals(this.address, address)) {
                        state = oldState.aliveAgain(generation);
                    } else {
                        state = new NodeState(health, generation, null, null);
                    }
                }
                NodeState newState = this.states.merge(address, state, NodeState::merge);
                if (!Objects.equals(oldState, newState)) {
                    System.out.println(address + " => " + newState);
                }
            }
        } catch (IOException ex) {
            // this is fine - just means we're done with handling events
        }
    }

    enum NodeHealth {
        ALIVE,
        SUSPICIOUS,
        DEAD,
        LEFT
    }

    static class NodeAddress {
        final Inet4Address address;
        final short port;

        public NodeAddress(Inet4Address address, short port) {
            this.address = address;
            this.port = port;
        }

        @Override
        public boolean equals(Object o) {
            if (this == o) return true;
            if (o == null || getClass() != o.getClass()) return false;
            NodeAddress that = (NodeAddress) o;
            return port == that.port &&
                    address.equals(that.address);
        }

        @Override
        public int hashCode() {
            return Objects.hash(address, port);
        }

        @Override
        public String toString() {
            return "udp://" + address.getHostAddress() + ":" + (port & 0xFFFF);
        }
    }

    static class NodeState {
        final NodeHealth health;
        final byte generation;
        final Byte serviceByte;
        final Short servicePort;
        long timesMentioned;

        public NodeState(NodeHealth health, byte generation, Byte serviceByte, Short servicePort) {
            this.health = health;
            this.generation = generation;
            this.serviceByte = serviceByte;
            this.servicePort = servicePort;
            this.timesMentioned = 0;
        }

        NodeState merge(NodeState other) {
            if (isLaterGeneration(other.generation, this.generation)) {
                if (other.serviceByte == null) {
                    return new NodeState(other.health, other.generation, this.serviceByte, this.servicePort);
                } else {
                    return other;
                }
            } else if (other.health.ordinal() > this.health.ordinal()) {
                if (other.serviceByte == null) {
                    return new NodeState(other.health, other.generation, this.serviceByte, this.servicePort);
                } else {
                    return other;
                }
            } else {
                if (this.serviceByte == null) {
                    return new NodeState(this.health, this.generation, other.serviceByte, other.servicePort);
                } else {
                    return this;
                }
            }
        }

        public static boolean isLaterGeneration(byte gen1, byte gen2) {
            return ((0 < gen1 - gen2) && (gen1 - gen2 < 191)
                    || (gen1 - gen2 <= -191));
        }

        public NodeState aliveAgain(byte generation) {
            byte nextGeneration = (byte) (generation + 1);
            if (isLaterGeneration(this.generation, nextGeneration)) {
                nextGeneration = this.generation;
            }
            return new NodeState(NodeHealth.ALIVE, nextGeneration, this.serviceByte, this.servicePort);
        }

        @Override
        public boolean equals(Object o) {
            if (this == o) return true;
            if (o == null || getClass() != o.getClass()) return false;
            NodeState nodeState = (NodeState) o;
            return generation == nodeState.generation &&
                    health == nodeState.health &&
                    Objects.equals(serviceByte, nodeState.serviceByte) &&
                    Objects.equals(servicePort, nodeState.servicePort);
        }

        @Override
        public int hashCode() {
            return Objects.hash(health, generation, serviceByte, servicePort);
        }

        @Override
        public String toString() {
            return String.format("%s[%s]%s",
                    health, generation,
                    serviceByte == null ? "" : String.format("{%s:%s}", serviceByte, servicePort));
        }
    }

    private static class FiniteByteArrayOutputStream extends OutputStream {
        private final byte[] buffer;

        private int currentIndex = 0;
        public FiniteByteArrayOutputStream(byte[] buffer) {
            this.buffer = buffer;
        }

        @Override
        public void write(int b) throws IOException {
            if (this.currentIndex < buffer.length) {
                buffer[this.currentIndex++] = (byte) b;
            } else {
                throw new FiniteByteArrayOverflowException();
            }
        }

        public int position() {
            return this.currentIndex;
        }

        private static class FiniteByteArrayOverflowException extends IOException {

            @Override
            public synchronized Throwable fillInStackTrace() {
                return this;
            }
        }
    }

    private interface MessageWriter<T> {
        void writeTo(DataOutputStream dos) throws IOException;
    }

    public static void main(String[] args) throws Exception {
        Gossip gossip = new Gossip(
                (Inet4Address) Inet4Address.getByName("10.10.15.158"),
                (short) 5000,
                (byte) 1,
                (short) 8080);
        try {
            gossip.start();
            gossip.sendPing(
                    new NodeAddress(
                            (Inet4Address) Inet4Address.getByName("10.10.15.158"),
                            (short) 6000));
            while (true) {

            }
        } finally {
            gossip.stop();
        }
    }
}
