package com.rokt.gossip;

public interface Listener {
    void accept(NodeAddress from, NodeAddress address, NodeState newState, NodeState oldState);
}
