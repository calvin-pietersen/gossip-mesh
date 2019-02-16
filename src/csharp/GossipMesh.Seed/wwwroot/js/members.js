"use strict";

initialize_topo();

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

var connection = new signalR.HubConnectionBuilder().withUrl("/membersHub").build();

connection.on("InitializationMessage", function (graphData, memberEvents) {
    load(graphData);

    var dataSet = [];
    for (var i = 0, len = memberEvents.length; i < len; i++) {
       dataSet.push(memberEventToRecord(memberEvents[i]));
    }

    dataTable.rows.add(dataSet).draw();
});

connection.on("MemberStateUpdatedMessage", function (graphData, memberEvent) {
    load(graphData);
    dataTable.row.add(memberEventToRecord(memberEvent)).draw();
});

connection.start().catch(function (err) {
    return console.error(err.toString());
});

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