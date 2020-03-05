package com.gossipmesh.core;

import java.io.IOException;

interface MessageConsumer<T> {
    void accept(T dos) throws IOException;
}
