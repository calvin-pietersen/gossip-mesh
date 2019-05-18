# Gossip mesh protocol

This gossip protocol is based off the [SWIM paper][SWIM]. It is a
simple membership protocol used for failure detection and service
discovery.

There are two interconnected components to this protocol: one which
handles failure detection, and one which handles discovery and
membership. Each of them rely on information provided by the other,
and each gossip message contains information from and to both
components.

    +-------------------------------------------------------------+
    | +-----------+  node suspicious, or dead  +---------------+  |
    | |  Failure  | -------------------------> |  Membership   |  |
    | | detection | <------------------------- | and discovery |  |
    | +-----------+   node added, or removed   +---------------+  |
    +-------------------------------------------------------------+
    Node A
                           ^              |
                           |    gossip    |
                           |   messages   |
                           |              v
    Node B
    +-------------------------------------------------------------+
    | +-----------+  node suspicious, or dead  +---------------+  |
    | |  Failure  | -------------------------> |  Membership   |  |
    | | detection | <------------------------- | and discovery |  |
    | +-----------+   node added, or removed   +---------------+  |
    +-------------------------------------------------------------+

The failure detection component is responsible for determining whether
nodes have failed. This is done by periodically sending messages to
other nodes, and keeping track of their responses. This also involves
responding to messages from other nodes to provide evidence that we
have not failed. In order to know which nodes exist (and thus can be
sent messages), the failure detection component consumes information
about nodes from the membership and discovery component.

The membership and discovery component is responsible for building a
database of the current state of all known nodes. This involves
sending messages providing a subset of our local database, and
consuming messages from other nodes doing the same. The failure
detection component provides additional information about nodes which
have failed, which is then captured in the database (and subsequently
propagated to other nodes).

[SWIM]: http://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf

## Message format

Each message that is sent consists of a version number, two parts
corresponding to the two components described above.

    +------------------+------------------------+-------------------------------+
    | Version (1 byte) | Failure detection data | Membership and discovery data |
    +------------------+------------------------+-------------------------------+

The current protocol version number is `1`.

The failure detection and membership data segments are each variable
number of bytes long, and are explained in the following sections.

Each message is up to `508` bytes is length, so as to always fit into
a single UDP packet.

## Failure detection

In order to quickly detect failures in the cluster, each message is
part of a protocol of pinging and acknowledging. At any point a node
`A` may receive a `ping` message from another node, `B`, to which it
must reply with an `ack`.

          ping
      ----------->
    A              B
      <-----------
           ack

If `B` does not reply with an `ack` within a given (configurable) time
frame, then `A` will notify its Membership and Discovery component
that `B` is suspicious. `A` will then perform an "indirect ping"
through another node, `C`. This is done by requesting that `C` ping
`B`. `C` pings `B`, mentioning that the request originated at
`A`. When `B` replies to the ping it asks `C` to forward the request
back to `A`, mentioning that it came from `B`.

If `A` does not receive a reply to its "indirect ping" within a given
(configurable) time frame, then `A` will notify its Membership and
Discovery component that `B` is dead.

These indirect pings are most easily visualised in the following way:

      req(B, ping)   fwd(A, ping)
      ----------->   ----------->
    A              C              B
      <-----------   <-----------
       fwd(B, ack)    req(A, ack)

`A` may choose to send multiple indirect pings to `B` through nodes `C₁`, `C₂`, ..., `Cₙ`.

These messages are encoded as follows:

| Message            | First byte | IP      | Port                | Notes              |
|--------------------|------------|---------|---------------------|--------------------|
| `ack`              | 0x00       | N/A     | N/A                 |                    |
| `ping`             | 0x01       | N/A     | N/A                 |                    |
| `request` `ack`    | 0x04       | 4 bytes | 2 bytes, big endian | goes to IP/port    |
| `request` `ping`   | 0x05       | 4 bytes | 2 bytes, big endian | goes to IP/port    |
| `forwarded` `ack`  | 0x06       | 4 bytes | 2 bytes, big endian | comes from IP/port |
| `forwarded` `ping` | 0x07       | 4 bytes | 2 bytes, big endian | comes from IP/port |

Each node should periodically send `ping` messages to a random subset
of nodes known to the Membership and Discovery component. The period
at which `ping`s are sent is called the _protocol period_. The number
of peers that are sent messages per protocol period is called the
_fanout factor_.

## Membership and Discovery

The process of membership and discovery aims to disseminate
information between nodes about the state of other nodes in the
cluster. There are three main components to the information
communicated about nodes:

  - their current state; that is, whether the nodes are alive,
    suspicious, dead, or whether they have left the cluster

  - the service exposed by each node, and the port on which that
    service is exposed (the IP is assumed to be the same as the
    gossiper)

Each node also has a _generation_, which is used to determine the
ordering of events. `A`'s generation value should only ever be changed
by `A` itself.

The membership and discovery portion of a message has three parts:

  - information about the sending node
  - information that the sender believes to be true about the
    receiving node
  - information that the sender believes to be true about arbitrary
    other nodes (repeated until the message is full)

They have the following components:

| Type     | IP      | Port                | State                        | Generation | Service | Service port        |
|----------|---------|---------------------|------------------------------|------------|---------|---------------------|
| sender   | N/A     | N/A                 | 0 bytes (implicitly `alive`) | 1 byte     | 1 byte  | 2 bytes, big endian |
| receiver | N/A     | N/A                 | 1 byte                       | 1 byte     | N/A     | N/A                 |
| other    | 4 bytes | 2 bytes, big endian | 1 byte                       | 1 byte     | 1 byte  | 2 bytes, big endian |

When a sender is first contacting another node (ie. before joining the
network) they may not have any state or generation information for the
receiving node. In this case they should assume the receiver is `dead`
and at generation `0` (which will be refuted by the receiver).

### Node states

There are four possible node states:

  - _alive_ (`0x00`): the node is alive and can be communicated with
  - _suspicious_ (`0x01`): the node has stopped responding, but we're
    not sure if it's dead yet
  - _dead_ (`0x02`): the node has died
  - _left_ (`0x03`): the node has voluntarily left the cluster

### Updating membership information

When a node `A` receives an update about node `B` it must decide how
to reconcile this new information with the information that it
currently believes to be true. This is done by merging the two pieces
of information and using the "winner" to override any applicable
fields in the "loser".

The winner is determined by comparing the generations first, followed
by the states.

  - Generations are compared using a circular comparison, so
    incrementing the generation (mod 255) will always produce a
    "higher" generation than before. The comparison operation can be
    defined like this (in Java - note that byte operations are
    promoted to int):

        // is `gen1` later than `gen2`?
        boolean isLaterGeneration(byte gen1, byte gen2) {
            return ((0 < gen1 - gen2) && (gen1 - gen2 < 191)
                   || (gen1 - gen2 <= -191));
        }

  - If the generations are equal, then the states are compared. The
    states follow the following inequality:

        alive < suspicious < dead < left

While `A` should not make any decisions to change the generation of
any other nodes, it may mark other nodes as `suspicious` or `dead`
based on the judgements of the Failure Detection component. If those
other nodes are still alive then they will refute `A`s judgement by
increasing their generation and declaring themselves to be `alive`.

When `A` wants to refute another node calling it suspicious/dead, it
must ensure that its generation is later than the generation of the
suspicious/dead data. For example: if `A` receives a message claiming
that it is suspicious at generation `3`, it should increment its
generation to `4` before sending any more messages. If its generation
is already greater than `3` then it can continue to use its current
generation.

<!-- Local Variables: -->
<!-- eval: (flycheck-mode 1) -->
<!-- End: -->
