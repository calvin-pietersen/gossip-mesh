# Gossip mesh protocol

This gossip protocol is based off the [SWIM paper][SWIM]. It is a
simple membership protocol used for failure detection and service
discovery.

## Discovery and membership

Members of the cluster communicate to each other by "gossiping"
information. Communication about the current state of
nodes is communicated using events. There are four event types:

 * _alive_: the node is alive and can be communicated with
 * _suspicious_: the node has stopped responding, but we're not sure
   if it's dead yet
 * _dead_: the node has died
 * _left_: the node has voluntarily left the cluster

These events have the following format:

| Field            | Size    | Notes                                                   |
|------------------|---------|---------------------------------------------------------|
| type             | 1 byte  | alive = 0, suspicious = 1, dead = 2, left = 3           |
| node ip address  | 4 bytes | IP4 only                                                |
| node gossip port | 2 bytes | big endian                                              |
| node generation  | 1 byte  | used to supersede events - see below                    |
| service          | 1 byte  | only for _alive_ events, meaning defined by application |
| service port     | 2 bytes | only for _alive_ events, big endian                     |

Thus, _alive_ events are 11 bytes long, and all other events are 8
bytes long.

An example alive event might look like this:

    +---+-----+-----+----+-----+----+-----+----+---+---+----+
    | 0 | 192 | 168 | 63 | 174 | 19 | 159 | 73 | 1 | 0 | 80 |
    +---+-----+-----+----+-----+----+-----+----+---+---+----+

Most of these fields should be relatively self-explanatory, but the
/node generation/ warrants explanation. Due to the mechanics of a
gossip protocol, we have no guarantees about when, or how many times,
an event is sent to a particular node. We may receive the same event
several times, and there is no guarantee that events will always be
up-to-date. We may be told that node `A` is _dead_, and then receive a
subsequent event telling that node `A` is _alive_. How can we tell
which is the current state?

To resolve this difficulty, we add a _node generation_ to events. The
_node generation_ associated with a given node can only ever be
incremented by _that node_. Thus, only node `A` may send an event with
a generation number different to the events it has received about `A`.
`A` must increment its generation number to refute events from other
nodes about itself. If `A` receives an event stating that "`A` is
suspicious" then it should immediately begin disseminating an event of
"`A` is alive", with a higher generation number than the event it
received.

Events invalidate each other according to the following rules:
 * event `X` invalidates event `Y` if the node generation of `X` is
   "greater" than the node generation of `Y` (see below for the
   details of this comparison),
 * if event `X` and `Y` have an equal node generation, then event `X`
   invalidates event `Y` if its type is "higher" according to the
   following sequence: _alive_ < _suspicious_ < _dead_ < _left_,
 * otherwise, event `Y` invalidates event `X` (in the case of equal
   events, either event may be chosen arbitrarily)

Nodes should only send events that have not been invalidated.

Generations are compared using a circular comparison, so incrementing
the generation (mod 255) will always produce a "higher" generation
than before. The comparison operation can be defined like this (in
Java - note that byte operations are promoted to int):

    // is `gen1` later than `gen2`?
    boolean isLaterGeneration(byte gen1, byte gen2) {
        return ((0 < gen1 - gen2) && (gen1 - gen2 < 191)
                || (gen1 - gen2 <= -191));
    }

Events are sent by being _disseminated_ in messages, which are also
used for failure detection. See the following sections for an
explanation of messages.

## Failure detection

Random probing is used to provide efficient failure detection. During
each protocol period, each node will send out UDP packets to some
subset of their known peers. If these peers respond, then they will be
considered _alive_. If they don't respond they will be considered
_suspicious_. If they continue to be unresponsive for an extended
period then they will be considered _dead_.

Messages are used to _disseminate_ the events described in the
previous section. The first bytes of a message carry information
related to failure detection (the details of which will be specified
below), but the remainder of the 508 byte UDP payload is then filled
with as many events as can fit. The particular strategy to chose
messages is client-dependent, but it is advised that clients
prioritise sending: (a) information about themselves, and (b) messages
that they have sent comparatively few times.

Each message will look something like this:

    +------------------------+-------+-------+-------+-----+
    | Failure detection data | Event | Event | Event | ... |
    +------------------------+-------+-------+-------+-----+

The size of each event is discussed above, and the size of the failure
detection data is discussed below.

### Direct health check

Every protocol period, each member `A` will randomly pick one (or
more) peer(s) to _ping_. It sends a _ping_ message to each chosen
peer, `B`. `B` will then respond to `A` with an _ack_ to communicate
that it is still alive.

            ping
       ------------->
    A                  B
       <-------------
            ack

A _ping_ and an _ack_ each have a single byte of overhead. If the
first byte of a message is `0` then it is an _ack_. If the first byte
of a message is `1` then it is a _ping_.

Thus, we have the following structure:

| Field | Size    | Notes                       |
|-------|---------|-----------------------------|
| type  | 1 byte  | ack = 0, ping = 1 |

### Indirect health check

If node `B` has not responded to a _ping_, node `A` may request other
nodes to ping it on behalf of node `A`. This method of indirect
pinging aims to detect when a node is still alive, but for some reason
node `A` is unable to communicate with it (presumably due to some sort
of networking issue).

To do this, node `A` randomly picks one (or more) peer(s) to ask to
ping `B`. It sends a "ping request" to each chosen peer, `C`. `C` will
then forward the message on to `B` (as well as consuming any events in
the message). When `B` responds, instead of sending a plain _ack_ it
will send an "ack request", which `C` will forward along to `A`
(again, consuming any events in the message).

          ping-req        forwarded-ping
       ------------->     ------------->
    A                  C                  B
       <-------------     <-------------
        forwarded-ack         ack-req

The structure of the "ping request" and the "forwarded ping" messages
is identical, and the structure of the "ack request" and the
"forwarded ack" messages is identical. They are also very similar to
each other:

| Field                  | Size    | Notes                                 |
|------------------------|---------|---------------------------------------|
| type                   | 1 byte  | forwarded ack = 2, forwarded ping = 3 |
| destination ip address | 4 bytes |                                       |
| destination port       | 2 bytes | big endian                            |
| source ip address      | 4 bytes |                                       |
| source port            | 2 bytes | big endian                            |

A forwarded message contains the IP address and port of the source
(ie. `A`), and the IP address of the destination (ie. `B`) to enable
routing that does not depend on the state of the intermediate nodes
(ie. so nodes don't have to remember where to forward messages).

[SWIM]: http://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf
