using System.Net;
using System.Threading.Tasks;

namespace GossipMesh.Core
{
    public interface IListener
    {
        Task Accept(NodeEvent nodeEvent);
    }
}