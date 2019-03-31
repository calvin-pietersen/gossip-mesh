using System.Net;
using System.Threading.Tasks;

namespace GossipMesh.Core
{
    public interface IMemberListener
    {
        Task MemberUpdatedCallback(MemberEvent memberEvent);
    }
}