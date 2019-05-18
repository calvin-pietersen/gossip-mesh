package com.rokt.gossip;

import java.net.Inet4Address;
import java.util.Objects;

public class MemberAddress {
    public final Inet4Address address;
    public final short port;

    public MemberAddress(Inet4Address address, short port) {
        this.address = address;
        this.port = port;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null || getClass() != o.getClass()) return false;
        MemberAddress that = (MemberAddress) o;
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
