# Gossip Mesh Protocol

## Messages
Used by the failure detector and encapsulates events for membership state dissemination.

Standard health check
```
   ping
   A------------>B

                ack
   A<------------B
```
Forwarded health check
```
   ping
   A------x      B


   ping request
           C
         /   \
       /       \
     /           \
   A              >B
     \           /
       \       /
         \   /
           D

         ack request
           C
         /   \
       /       \
     /           \
   A<              B
     \           /
       \       /
         \   /
           D
           
```

### Ping
A direct request from one gossiper to another. The pinger sends a ping to a pingee to see if the pingee is alive. This is the start of the failure detection process.

1. type: 1
2. repeated events

### Ack
A direct response from the pingee to the pinger to acknoledge their ping.

1. type: 3
2. repeated events

### Ping Request
In the case where a pinger did not recieve an ack back from the pingee within the ping timeout, they will send a ping request to k members (forwarder) in an attempt to indirectly ping the pingee.

1. type: 2
2. destination ip
3. destination gossiper port
4. source ip
5. source port
6. repeated events

### Ack Request
In the case of the pingee recieving a ping request from a forwarder, they will indirectly respond to the original pinger with an ack request through the forwarder.

1. type: 4
2. destination ip
3. destination gossiper port
4. repeated events

## Events
Captures state changes of members.

### Alive

1. type: 1
2. ip
3. gossiper port
4. service port
5. service id
6. generation

### Left

1. type: 2
2. ip
3. gossiper port

### Dead

1. type: 3
2. ip
3. gossiper port

### Suspect

1. type: 3
2. ip
3. gossiper port