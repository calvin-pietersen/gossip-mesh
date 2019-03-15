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

var connection = new signalR.HubConnectionBuilder().withUrl("/membersHub").build();

connection.on("InitializationMessage", function (graph, memberEvents) {

    for (var i = 0, len = graph.nodes.length; i < len; i++) {
        nodes[graph.nodes[i].id] = graph.nodes[i];
    }

    load(graph.nodes);
    addMemberEventsToTable(memberEvents);
});

connection.on("MemberEventMessage", function (memberEvent) {
    dataTable.row.add(memberEventToRecord(memberEvent)).draw();
});

connection.on("NodeUpdatedMessage", function (node) {
    nodes[node.id] = node;
    load(Object.values(nodes));
});

connection.start().catch(function (err) {
    return console.error(err.toString());
});

function addMemberEventsToTable(memberEvents) {
    var dataSet = [];
    for (var i = 0, len = memberEvents.length; i < len; i++) {
       dataSet.push(memberEventToRecord(memberEvents[i]));
    }

    dataTable.rows.add(dataSet).draw();
}

function memberEventToRecord(memberEvent) {
    return [
            memberEvent.receivedDateTime,
            memberEvent.senderGossipEndPoint,
            memberEvent.ip,
            memberEvent.state,
            memberEvent.gossipPort,
            memberEvent.generation,
            memberEvent.service,
            memberEvent.servicePort
    ]
}