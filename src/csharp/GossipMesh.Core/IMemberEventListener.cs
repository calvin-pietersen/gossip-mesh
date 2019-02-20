using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace GossipMesh.Core
{
    public interface IMemberEventsListener
    {
        Task MemberEventsCallback(IEnumerable<MemberEvent> memberEvents);
    }
}