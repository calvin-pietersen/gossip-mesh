# Gossip Mesh Protocol
Gossip Mesh is based off the [SWIM] paper. It is a simple membership protocol used for failure detection and service discovery.

## Direct health check
Every round of gossip, each member (A) will randomly pick another member (B) to ping. B will then ack A to let them know they are alive.
```
   ping
   A------------------>B

                     ack
   A<------------------B
```

## Indirect health check
In a round of gossip, if B does not respond to the ping of A within the ping timout period, A will try to indirectly ping B. It does this by sending a ping request through k other members (C & D), who will then try to forward the message through to B. B will then attempt to directly ack A and indirectly ack A via C & D with an ack request.

```
   ping
   A---------x         B

   OR
                     ack
   A         x---------B

   ping request
             C
           /   \
         /       \
       /           \
   A---             -->B
       \           /
         \       /
           \   /
             D

             ack request
             C
           /   \
         /       \
       /           \ ack
   A<------------------B
       \           /
         \       /
           \   /
             D
           
```

## Messages
Used by the failure detector and encapsulates events for membership state dissemination.

### Ping
A direct request from one gossiper (A) to another (B). A sends a ping to a B to see if B is alive. This is the start of the failure detection process.

1. type: 1 (1 Byte)
2. repeated events (511 Bytes max)

### Ack
A direct response from B to A to acknoledge their ping.

1. type: 3 (1 Byte)
2. repeated events (511 Bytes max)

### Ping Request
In the case where A did not recieve an ack back from B within the ping timeout, they will send a ping request to k members in an attempt to indirectly ping B.

1. type: 2 (1 Byte)
2. destination ip (4 Bytes)
3. destination gossiper port (2 Bytes)
4. source ip (4 Bytes)
5. source gossiper port (2 Bytes)
6. repeated events (499 Bytes mnax)

### Ack Request
In the case of B recieving a ping request from C or D, they will indirectly respond to the original pinger with an ack request through C or D.

1. type: 4 (1 Byte)
2. destination ip (4 Bytes)
3. destination gossiper port (2 Byes)
4. repeated events (505 Bytes max)

## Events
Captures the state changes of members.

### Alive

1. type: 1 (1 Byte)
2. ip (4 Bytes)
3. gossiper port (2 Bytes)
4. service port (2 Bytes)
5. service id (1 Byte)
6. generation (1 Byte)

### Left

1. type: 2 (1 Byte)
2. ip (4 Bytes)
3. gossiper port (2 Bytes)

### Dead

1. type: 3 (1 Byte)
2. ip (4 Bytes)
3. gossiper port (2 Bytes)

### Suspected

1. type: 4 (1 Byte)
2. ip (4 Bytes)
3. gossiper port (2 Bytes)



[SWIM]: http://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf 