package com.gossipmesh.core;

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

public class Gossiper {
    private static final Logger LOGGER = Logger.getLogger(Gossiper.class.getCanonicalName());
    private final Map<MemberAddress, Member> members;
    private final Map<MemberAddress, ScheduledFuture> waiting;
    private final byte serviceByte;
    private final short servicePort;
    private final GossiperOptions options;
    private final ScheduledExecutorService executor;
    private final DatagramSocket socket;
    private final HashMap<Object, Listener> listeners;
    private Thread listener;
    private byte generation;

    public Gossiper(int serviceByte, int servicePort, GossiperOptions options) throws IOException {
        this(new DatagramSocket(), serviceByte, servicePort, options);
    }

    public Gossiper(int port, int serviceByte, int servicePort, GossiperOptions options) throws IOException {
        this(new DatagramSocket(port), serviceByte, servicePort, options);
    }

    public Gossiper(DatagramSocket socket, int serviceByte, int servicePort, GossiperOptions options) {
        this.members = new HashMap<>();
        this.waiting = new HashMap<>();
        this.serviceByte = (byte) serviceByte;
        this.servicePort = (short) servicePort;
        this.options = options;
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
        }), 0, options.getProtocolPeriodMs(), TimeUnit.MILLISECONDS);
        socket.setSoTimeout(500);
        this.listener = new Thread(() -> {
            byte[] buffer = new byte[508];
            while (!Thread.currentThread().isInterrupted()) {
                try {
                    DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                    socket.receive(packet);
                    assert (packet.getOffset() == 0);
                    MemberAddress address = new MemberAddress(
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

    private Iterable<MemberAddress> randomNodes() {
        return randomNodes((address, state) -> true);
    }

    private Iterable<MemberAddress> randomNodes(BiPredicate<MemberAddress, Member> matching) {
        return () -> this.members.entrySet()
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
        int i = options.getFanoutFactor();
        for (MemberAddress address : randomNodes()) {
            if (--i < 0) {
                break;
            }
            LOGGER.log(Level.FINEST, "Performing direct ping on: " + address);
            ping(address);
        }
    }

    private static MemberAddress parseAddress(DataInput stream) throws IOException {
        byte[] addressBytes = new byte[4];
        stream.readFully(addressBytes);
        return new MemberAddress(
                (Inet4Address) Inet4Address.getByAddress(addressBytes),
                stream.readShort());
    }

    private static void writeAddress(DataOutput output, MemberAddress address) throws IOException {
        output.write(address.address.getAddress());
        output.writeShort(address.port);
    }

    private static void writeEvent(DataOutput output, MemberAddress address, Member member) throws IOException {
        writeAddress(output, address);
        output.write(member.state.ordinal());
        output.write(member.generation);
        if (member.state == MemberState.ALIVE) {
            output.write(member.serviceByte);
            output.writeShort(member.servicePort);
        }
    }

    private void scheduleTask(MemberAddress address, Runnable command, int delay, TimeUnit units) {
        this.waiting.computeIfAbsent(address, a -> {
            ScheduledFuture<?>[] future = new ScheduledFuture<?>[1];
            future[0] = this.executor.schedule(loggingExceptions(() -> {
                this.waiting.remove(address, future[0]);
                command.run();
            }), delay, TimeUnit.MILLISECONDS);
            return future[0];
        });
    }

    private void sendMessage(MemberAddress address, MessageWriter<DataOutput> header) throws IOException {
        byte[] outBuffer = new byte[508];
        int limit = 0;
        Set<MemberAddress> sending = new HashSet<>();
        try (FiniteByteArrayOutputStream os = new FiniteByteArrayOutputStream(outBuffer);
             DataOutputStream dos = new DataOutputStream(os)) {
            dos.write(0); // protocol version
            header.writeTo(dos);

            dos.write(this.generation);
            dos.write(this.serviceByte);
            dos.writeShort(this.servicePort);

            Member receiver = this.members.get(address);
            if (receiver == null) {
                dos.write(MemberState.DEAD.ordinal());
                dos.write(0);
            } else {
                dos.write(receiver.state.ordinal());
                dos.write(receiver.generation);
            }

            limit = os.position();

            PriorityQueue<Map.Entry<MemberAddress, Member>> queue = new PriorityQueue<>(Comparator.comparingLong(a -> a.getValue().timesMentioned));
            queue.addAll(this.members.entrySet());
            for (Map.Entry<MemberAddress, Member> entry : queue) {
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

        for (MemberAddress sent : sending) {
            this.members.get(sent).timesMentioned++;
        }
    }

    public void connectTo(Inet4Address address, int port) {
        this.executor.execute(loggingExceptions(() -> {
            try {
                this.ping(new MemberAddress(address, (short) port));
            } catch (IOException ex) {
                LOGGER.log(Level.SEVERE, "Exception thrown while connecting to " + address, ex);
            }
        }));
    }

    private void ping(MemberAddress address) throws IOException {
        sendMessage(address, dos -> dos.write(0x01));
        Member member = updateMember(null, address, m -> m == null ? new Member(MemberState.DEAD, (byte) 0, (byte) 0, (byte) 0) : m);
        if (member != null) {
            scheduleTask(address, () -> {
                try {
                    indirectPing(address, member);
                } catch (IOException ex) {
                    LOGGER.log(Level.SEVERE, "Exception thrown while performing indirect ping of " + address, ex);
                }
            }, options.getPingTimeoutMs(), TimeUnit.MILLISECONDS);
        }
    }

    private void indirectPing(MemberAddress address, Member member) throws IOException {
        updateMember(null, address, m -> m == null ? null : m.merge(member.withState(MemberState.SUSPICIOUS)));
        int i = options.getNumberOfIndirectEndPoints();
        for (MemberAddress relay : randomNodes((a, m) -> !Objects.equals(a, address))) {
            if (--i < 0) {
                break;
            }
            sendMessage(relay, dos -> {
                dos.write(0x03);
                writeAddress(dos, address);
            });
        }
        scheduleTask(address, () -> {
            Member dead = updateMember(null, address, m -> m == null ? null : m.merge(member.withState(MemberState.DEAD)));
            scheduleTask(address, () -> {
                // only prune the state if it hasn't changed
                updateMember(null, address, m -> Objects.equals(m, dead) ? null : m);
            }, options.getDeathTimeoutMs(), TimeUnit.MILLISECONDS);
        }, options.getIndirectPingTimeoutMs(), TimeUnit.MILLISECONDS);
    }

    private void handleMessage(MemberAddress address, DataInput input) throws IOException {
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

    private void removeAndCancel(MemberAddress address) {
        ScheduledFuture<?> f = this.waiting.remove(address);
        if (f != null) {
            f.cancel(true);
        }
    }

    private void handleDirectAck(MemberAddress address, DataInput input) throws IOException {
        removeAndCancel(address); // if we were waiting to hear from them - here they are!
        handleEvents(address, input);
    }

    private void handleDirectPing(MemberAddress address, DataInput input) throws IOException {
        removeAndCancel(address); // if we were waiting to hear from them - here they are!
        handleEvents(address, input);
        sendMessage(address, dos -> dos.write(0x00));
    }

    private void handleRequest(MemberAddress address, DataInput input, byte b) throws IOException {
        removeAndCancel(address); // if we were waiting to hear from them - here they are!
        MemberAddress destination = parseAddress(input);
        handleEvents(address, input);

        sendMessage(address, dos -> {
            dos.write(b | 0x06);
            writeAddress(dos, destination);
        });
    }

    private void handleForwarded(MemberAddress address, DataInput input, byte b) throws IOException {
        removeAndCancel(address); // if we were waiting to hear from them - here they are!
        MemberAddress source = parseAddress(input);
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

    private Member updateMember(MemberAddress from, MemberAddress address, Function<Member, Member> update) {
        Member oldMember = this.members.get(address);
        Member newMember = update.apply(oldMember);
        if (!Objects.equals(oldMember, newMember)) {
            if (newMember == null) {
                this.members.remove(address);
            } else {
                this.members.put(address, newMember);
            }
            notifyListeners(from, address, newMember, oldMember);
        }
        return newMember;
    }

    private void handleEvents(MemberAddress from, DataInput input) {
        try {
            byte senderGeneration = input.readByte();
            byte senderService = input.readByte();
            short senderServicePort = input.readShort();
            Member senderMember = new Member(MemberState.ALIVE,
                    senderGeneration,
                    senderService,
                    senderServicePort);
            updateMember(null, from, m -> m == null ? senderMember : m.merge(senderMember));

            MemberState myState = MemberState.values()[input.readByte()];
            byte myGeneration = input.readByte();
            if (myState == MemberState.SUSPICIOUS || myState == MemberState.DEAD) {
                byte newGeneration = (byte) (myGeneration + 1);
                if (Member.isLaterGeneration(generation, newGeneration)) {
                    newGeneration = generation;
                }
                this.generation = newGeneration;
            }

            // This is an infinte loop that will be broken when we hit an exception,
            // because DataInput doesn't let us see whether we're at the end
            //noinspection InfiniteLoopStatement
            while (true) {
                MemberAddress address = parseAddress(input);
                MemberState state = MemberState.values()[input.readByte()];
                byte generation = input.readByte();
                byte serviceByte = (state == MemberState.ALIVE ? input.readByte() : 0);
                short servicePort = (state == MemberState.ALIVE ? input.readShort() : 0);
                Member newMember = new Member(state, generation, serviceByte, servicePort);

                updateMember(from, address, m -> {
                    if (m != null) {
                        return m.merge(newMember);
                    } else if (state == MemberState.ALIVE || state == MemberState.SUSPICIOUS) {
                        return newMember;
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

    private void notifyListeners(MemberAddress from, MemberAddress address, Member newState, Member oldState) {
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
