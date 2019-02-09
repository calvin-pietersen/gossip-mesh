"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/membersHub").build();

connection.on("MemberStateUpdatedMessage", function (memberEvent) {
    addToList(memberEvent);
});

connection.on("MemberStates", function (memberEvents) {
    for (var i = 0, len = memberEvents.length; i < len; i++) {
        addToList(memberEvents[i]);
    }
});

function addToList(memberEvent) {
    var li = document.createElement("li");
    li.textContent = JSON.stringify(memberEvent);
    document.getElementById("membersList").appendChild(li);
}

connection.start().catch(function (err) {
    return console.error(err.toString());
});