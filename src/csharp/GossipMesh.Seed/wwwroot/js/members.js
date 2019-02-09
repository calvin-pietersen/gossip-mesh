"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/membersHub").build();

connection.on("MemberStateUpdatedMessage", function (senderGossipEndPoint, memberEvent) {
    var li = document.createElement("li");
    li.textContent = senderGossipEndPoint + " knows " + memberEvent;
    document.getElementById("membersList").appendChild(li);
});

connection.start().catch(function (err) {
    return console.error(err.toString());
});