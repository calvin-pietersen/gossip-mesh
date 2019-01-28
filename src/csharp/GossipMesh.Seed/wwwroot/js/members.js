"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/membersHub").build();

connection.on("MemberStateUpdatedMessage", function (message) {
    var li = document.createElement("li");
    li.textContent = message;
    console.log(message);
    document.getElementById("membersList").appendChild(li);
});

connection.start().catch(function (err) {
    return console.error(err.toString());
});