package com.rokt.gossip;

public interface Listener {
    void accept(MemberAddress from, MemberAddress address, Member newMember, Member oldMember);
}
