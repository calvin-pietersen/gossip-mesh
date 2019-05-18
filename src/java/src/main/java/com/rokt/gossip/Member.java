package com.rokt.gossip;

import java.util.Objects;

public class Member {
    public final MemberState state;
    final byte generation;
    public final byte serviceByte;
    public final short servicePort;
    long timesMentioned;

    Member(MemberState state, byte generation, byte serviceByte, short servicePort) {
        this.state = state;
        this.generation = generation;
        this.serviceByte = serviceByte;
        this.servicePort = servicePort;
        this.timesMentioned = 0;
    }

    Member merge(Member other) {
        if (isLaterGeneration(other.generation, this.generation)) {
            return other;
        } else if (other.state.ordinal() > this.state.ordinal()) {
            return other;
        } else {
            return this;
        }
    }

    Member withHealth(MemberState state) {
        return new Member(state, this.generation, this.serviceByte, this.servicePort);
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
        Member nodeState = (Member) o;
        return generation == nodeState.generation &&
                state == nodeState.state &&
                serviceByte == nodeState.serviceByte &&
                servicePort == nodeState.servicePort;
    }

    @Override
    public int hashCode() {
        return Objects.hash(state, generation, serviceByte, servicePort);
    }

    @Override
    public String toString() {
        return String.format("%s[%s]{%s:%s}",
                state, generation,
                serviceByte, servicePort);
    }
}
