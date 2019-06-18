package com.gossipmesh.core;

import java.io.IOException;

interface MessageWriter<T> {
    void writeTo(T dos) throws IOException;
}
