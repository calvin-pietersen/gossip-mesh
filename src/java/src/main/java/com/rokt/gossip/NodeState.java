package com.rokt.gossip;

import java.util.Objects;

public class NodeState {
    public final NodeHealth health;
    final byte generation;
    public final byte serviceByte;
    public final short servicePort;
    long timesMentioned;

    NodeState(NodeHealth health, byte generation, byte serviceByte, short servicePort) {
        this.health = health;
        this.generation = generation;
        this.serviceByte = serviceByte;
        this.servicePort = servicePort;
        this.timesMentioned = 0;
    }

    NodeState merge(NodeState other) {
        if (isLaterGeneration(other.generation, this.generation)) {
            return other;
        } else if (other.health.ordinal() > this.health.ordinal()) {
            return other;
        } else {
            return this;
        }
    }

    NodeState withHealth(NodeHealth health) {
        return new NodeState(health, this.generation, this.serviceByte, this.servicePort);
    }

    // is `gen1` later than `gen2`?
    static boolean isLaterGeneration(byte gen1, byte gen2) {
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
                serviceByte == nodeState.serviceByte &&
                servicePort == nodeState.servicePort;
    }

    @Override
    public int hashCode() {
        return Objects.hash(health, generation, serviceByte, servicePort);
    }

    @Override
    public String toString() {
        return String.format("%s[%s]{%s:%s}",
                health, generation,
                serviceByte, servicePort);
    }
}
