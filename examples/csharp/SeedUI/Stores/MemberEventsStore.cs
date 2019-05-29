using GossipMesh.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace GossipMesh.SeedUI.Stores
{
    public class MemberEventsStore : IMemberEventsStore
    {
        private readonly object _memberEventsLocker = new Object();
        private readonly List<MemberEvent> _memberEvents = new List<MemberEvent>();

        public void Add(MemberEvent memberEvent)
        {
            _memberEvents.Add(memberEvent);
        }

        public MemberEvent[] GetAll()
        {
            lock (_memberEventsLocker)
            {
                return _memberEvents.ToArray();
            }
        }
    }
}