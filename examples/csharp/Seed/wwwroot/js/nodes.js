"use strict";

const dataTable = $('#realtime').DataTable({
    columns: [
        { title: 'Received'},
        { title: 'Sender Gossip EndPoint' },
        { title: 'IP'},
        { title: 'State'},
        { title: 'Gossip Port'},
        { title: 'Generation'},
        { title: 'Service'},
        { title: 'Service Port'}
    ],
    order: [[ 0, "desc" ]]
});

initialize_topo();

var nodes = {};

var connection = new signalR.HubConnectionBuilder().withUrl("/nodesHub").build();

connection.on("InitializationMessage", function (graph, nodeEvents) {

    for (var i = 0, len = graph.nodes.length; i < len; i++) {
        nodes[graph.nodes[i].id] = graph.nodes[i];
    }

    load(graph.nodes);
    addNodeEventsToTable(nodeEvents);
});

connection.on("NodeUpdatedMessage", function (nodeEvent, node) {
    dataTable.row.add(nodeEventToRecord(nodeEvent)).draw();

    if (node.state === "Pruned") {
        delete nodes[node.id];
    } else {
        nodes[node.id] = node;
    }

    load(Object.values(nodes));
});

connection.start().catch(function (err) {
    return console.error(err.toString());
});

function addNodeEventsToTable(nodeEvents) {
    var dataSet = [];
    for (var i = 0, len = nodeEvents.length; i < len; i++) {
       dataSet.push(nodeEventToRecord(nodeEvents[i]));
    }

    dataTable.rows.add(dataSet).draw();
}

function nodeEventToRecord(nodeEvent) {
    return [
            nodeEvent.receivedDateTime,
            nodeEvent.senderGossipEndPoint,
            nodeEvent.ip,
            nodeEvent.state,
            nodeEvent.gossipPort,
            nodeEvent.generation,
            nodeEvent.service,
            nodeEvent.servicePort
    ]
}