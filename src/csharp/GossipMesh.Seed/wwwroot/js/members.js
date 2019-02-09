"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/membersHub").build();

connection.on("MemberStateUpdatedMessage", function (senderGossipEndPoint, memberEvent) {
    var li = document.createElement("li");
    li.textContent = JSON.stringify(senderGossipEndPoint) + " knows " + JSON.stringify(memberEvent);
    document.getElementById("membersList").appendChild(li);
});

connection.on("MemberStates", function (memberEvents) {
    var li = document.createElement("li");
    li.textContent = JSON.stringify(memberEvents);
    document.getElementById("membersList").appendChild(li);
});


connection.start().catch(function (err) {
    return console.error(err.toString());
});