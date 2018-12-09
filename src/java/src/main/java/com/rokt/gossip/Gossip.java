package com.rokt.gossip;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.Inet4Address;
import java.net.SocketTimeoutException;
import java.util.Arrays;
import java.util.HashMap;
import java.util.Map;
import java.util.Objects;

public class Gossip {
    private final Map<NodeAddress, NodeState> states;
    private final Inet4Address address;
    private final short port;
    private DatagramSocket socket;
    private Thread listenerThread;
    private byte currentGeneration;
    private final byte serviceByte;
    private final short servicePort;

    Gossip(Inet4Address address, short port, byte serviceByte, short servicePort) {
        this.address = address;
        this.port = port;
        this.states = new HashMap<>();
        this.listenerThread = null;
        this.serviceByte = serviceByte;
        this.servicePort = servicePort;
    }

    public void start() throws IOException {
        this.socket = new DatagramSocket(port, address);
        socket.setSoTimeout(500);
        this.listenerThread = new Thread(() -> {
            byte[] buffer = new byte[508];
            while (!Thread.currentThread().isInterrupted()) {
                try {
                    DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                    socket.receive(packet);
                    assert (packet.getOffset() == 0);
                    byte[] recvBuffer = Arrays.copyOf(packet.getData(), packet.getLength());
                    handleMessage((Inet4Address) packet.getAddress(), (short) packet.getPort(), recvBuffer);
                } catch (SocketTimeoutException ex) {
                    // do nothing
                } catch (IOException ex) {
                    ex.printStackTrace();
                }
            }
        });
        this.listenerThread.start();
        System.out.println("Started on " + this.address + ":" + this.port);
    }

    public void stop() throws InterruptedException {
        if (this.listenerThread != null) {
            this.listenerThread.interrupt();
            this.socket.close();
            this.listenerThread.join();
        }
    }

    void handleMessage(Inet4Address from, short port, byte[] buffer) throws IOException {
        switch (buffer[0]) {
            case 0: // direct ack
                handleDirectAck(from, port, buffer);
                break;
            case 1: // direct ping
                handleDirectPing(from, port, buffer);
                break;
            case 2: // indirect ack
                handleIndirectAck(from, port, buffer);
                break;
            case 3: // indirect ping
                handleIndirectPing(from, port, buffer);
                break;
        }
    }

    private static Inet4Address parseAddress(byte[] buffer, int index) throws IOException {
        return (Inet4Address) Inet4Address.getByAddress(new byte[]{
                buffer[index],
                buffer[index + 1],
                buffer[index + 2],
                buffer[index + 3]});
    }

    private static void writeAddress(byte[] buffer, int index, Inet4Address address) {
        byte[] addr = address.getAddress();
        buffer[index] = addr[0];
        buffer[index + 1] = addr[1];
        buffer[index + 2] = addr[2];
        buffer[index + 3] = addr[3];
    }

    private static short parsePort(byte[] buffer, int index) {
        // ugh - Java bit manipulation is gross
        return (short)(((buffer[index] & 0xFF) << 8) | (buffer[index + 1] & 0xFF));
    }

    private static void writePort(byte[] buffer, int index, short port) {
        int bigPort = port & 0xFFFF;
        buffer[index] = (byte) (bigPort >> 8);
        buffer[index + 1] = (byte) bigPort;
    }

    public void sendPing(Inet4Address destination, short port) throws IOException {
        // now ack the ping
        byte[] outBuffer = new byte[12];
        outBuffer[0] = 1;
        outBuffer[1] = 0;
        writeAddress(outBuffer, 2, this.address);
        writePort(outBuffer, 6, this.port);
        outBuffer[8] = this.currentGeneration;
        outBuffer[9] = this.serviceByte;
        writePort(outBuffer, 10, this.servicePort);


        DatagramPacket packet = new DatagramPacket(outBuffer, outBuffer.length);
        packet.setAddress(destination);
        packet.setPort(port);
        socket.send(packet);
    }

    private void handleDirectAck(Inet4Address from, short port, byte[] buffer) throws IOException {
        // We don't do failure detection logic, so we don't actually need the address/port
        handleEvents(buffer, 1);
    }

    private void handleDirectPing(Inet4Address from, short port, byte[] buffer) throws IOException {
        handleEvents(buffer, 1);

        // now ack the ping
        byte[] outBuffer = new byte[12];
        outBuffer[0] = 0;
        outBuffer[1] = 0;
        writeAddress(outBuffer, 2, this.address);
        writePort(outBuffer, 6, this.port);
        outBuffer[8] = this.currentGeneration;
        outBuffer[9] = this.serviceByte;
        writePort(outBuffer, 10, this.servicePort);


        DatagramPacket packet = new DatagramPacket(outBuffer, outBuffer.length);
        packet.setAddress(from);
        packet.setPort(port);
        socket.send(packet);
    }

    private void handleIndirectAck(Inet4Address from, short port, byte[] buffer) throws IOException {
        // We don't do failure detection logic, so we don't actually need the source/destination address/port
        handleEvents(buffer, 13);

        Inet4Address destinationAddress = parseAddress(buffer, 1);
        short destinationPort = parsePort(buffer, 5);

        DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
        packet.setAddress(destinationAddress);
        packet.setPort(destinationPort);
        socket.send(packet);
    }

    private void handleIndirectPing(Inet4Address from, short port, byte[] buffer) throws IOException {
        handleEvents(buffer, 13);

        Inet4Address destinationAddress = parseAddress(buffer, 1);
        short destinationPort = parsePort(buffer, 5);

        if (Objects.equals(this.address, destinationAddress) && Objects.equals(this.port, destinationPort)) {
            byte[] outBuffer = new byte[24];
            outBuffer[0] = 2;

            Inet4Address sourceAddress = parseAddress(buffer, 7);
            short sourcePort = parsePort(buffer, 11);

            writeAddress(outBuffer, 1, sourceAddress);
            writePort(outBuffer, 5, sourcePort);
            writeAddress(outBuffer, 7, destinationAddress);
            writePort(outBuffer, 11, destinationPort);

            outBuffer[13] = 0;
            writeAddress(outBuffer, 14, this.address);
            writePort(outBuffer, 18, this.port);
            outBuffer[20] = this.currentGeneration;
            outBuffer[21] = this.serviceByte;
            writePort(outBuffer, 22, this.servicePort);

            DatagramPacket packet = new DatagramPacket(outBuffer, outBuffer.length);
            packet.setAddress(from);
            packet.setPort(port);
            socket.send(packet);
        } else {
            DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
            packet.setAddress(destinationAddress);
            packet.setPort(destinationPort);
            socket.send(packet);
        }
    }

    private void handleEvents(byte[] buffer, int index) throws IOException {
        while (index < buffer.length) {
            NodeHealth health = NodeHealth.values()[buffer[index]];
            Inet4Address address = parseAddress(buffer, index + 1);
            short port = parsePort(buffer, index + 5);
            byte generation = buffer[index + 7];
            index += 8;

            NodeState state;
            if (health == NodeHealth.ALIVE) {
                byte serviceByte = buffer[index];
                short servicePort = parsePort(buffer, index + 1);
                index += 3;
                state = new NodeState(health, generation, serviceByte, servicePort);
            } else {
                if (Objects.equals(this.address, address) && Objects.equals(this.port, port)) {
                    state = new NodeState(NodeHealth.ALIVE, ++this.currentGeneration, this.serviceByte, this.servicePort);
                } else {
                    state = new NodeState(health, generation, null, null);
                }
            }
            NodeState oldState = this.states.get(new NodeAddress(address, port));
            NodeState newState = this.states.merge(new NodeAddress(address, port), state, NodeState::merge);
            if (!Objects.equals(oldState, newState)) {
                System.out.println(new NodeAddress(address, port) + " => " + newState);
            }
        }
    }

    enum NodeHealth {
        ALIVE,
        SUSPICIOUS,
        DEAD,
        LEFT
    }

    class NodeAddress {
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
            return "NodeAddress{" +
                    "address=" + address +
                    ", port=" + port +
                    '}';
        }
    }

    class NodeState {
        final NodeHealth health;
        final byte generation;
        final Byte serviceByte;
        final Short servicePort;

        public NodeState(NodeHealth health, byte generation, Byte serviceByte, Short servicePort) {
            this.health = health;
            this.generation = generation;
            this.serviceByte = serviceByte;
            this.servicePort = servicePort;
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

        boolean isLaterGeneration(byte gen1, byte gen2) {
            return ((0 < gen1 - gen2) && (gen1 - gen2 < 191)
                    || (gen1 - gen2 <= -191));
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
            return "NodeState{" +
                    "health=" + health +
                    ", generation=" + generation +
                    ", serviceByte=" + serviceByte +
                    ", servicePort=" + servicePort +
                    '}';
        }
    }

    public static void main(String[] args) throws Exception {
        Gossip gossip = new Gossip((Inet4Address) Inet4Address.getByName("192.168.10.199"), (short) 5001, (byte) 1, (short) 8080);
        gossip.start();
        gossip.sendPing((Inet4Address) Inet4Address.getByName("192.168.10.199"), (short) 5000);
        try {
            Thread.sleep(1000000);
        } finally {
            gossip.stop();
        }
    }
}
