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
    ]
});

var connection = new signalR.HubConnectionBuilder().withUrl("/membersHub").build();

connection.on("InitializationMessage", function (graphData, memberEvents) {
    load(graphData);

    for (var i = 0, len = memberEvents.length; i < len; i++) {
        addMemberEventToTable(memberEvents[i]);
    }
});

connection.on("MemberStateUpdatedMessage", function (graphData, memberEvent) {
    load(graphData);
    addMemberEventToTable(memberEvent);S
});

connection.start().catch(function (err) {
    return console.error(err.toString());
});

function addMemberEventToTable(memberEvent) {
    var record = [
        memberEvent.receivedDateTime,
        memberEvent.senderGossipEndPoint,
        memberEvent.ip,
        memberEvent.state,
        memberEvent.gossipPort,
        memberEvent.generation,
        memberEvent.service,
        memberEvent.servicePort
    ];
dataTable.row.add(record).draw();
}