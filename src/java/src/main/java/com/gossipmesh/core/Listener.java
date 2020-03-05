package com.gossipmesh.core;

public interface Listener {
    void accept(MemberAddress from, MemberAddress to, Member newMember, Member oldMember);
}
