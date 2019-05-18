package com.rokt.gossip;

import java.io.DataOutputStream;
import java.io.IOException;

interface MessageWriter<T> {
    void writeTo(T dos) throws IOException;
}
