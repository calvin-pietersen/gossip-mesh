package com.rokt.gossip;

import java.io.IOException;

interface MessageWriter<T> {
    void writeTo(T dos) throws IOException;
}
