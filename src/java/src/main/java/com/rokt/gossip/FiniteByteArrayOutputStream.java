package com.rokt.gossip;

import java.io.IOException;
import java.io.OutputStream;

class FiniteByteArrayOutputStream extends OutputStream {
    private final byte[] buffer;

    private int currentIndex = 0;

    public FiniteByteArrayOutputStream(byte[] buffer) {
        this.buffer = buffer;
    }

    @Override
    public void write(int b) throws IOException {
        if (this.currentIndex < buffer.length) {
            buffer[this.currentIndex++] = (byte) b;
        } else {
            throw new FiniteByteArrayOverflowException();
        }
    }

    public int position() {
        return this.currentIndex;
    }

    private static class FiniteByteArrayOverflowException extends IOException {

        @Override
        public synchronized Throwable fillInStackTrace() {
            return this;
        }
    }
}
