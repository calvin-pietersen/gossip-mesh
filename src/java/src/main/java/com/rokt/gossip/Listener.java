package com.gossipmesh.core;

public interface Listener {
    void accept(MemberAddress from, MemberAddress address, Member newMember, Member oldMember);
}
