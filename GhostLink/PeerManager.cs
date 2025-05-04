using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace GhostLink
{
    public class PeerManager
    {
        private Dictionary<IPAddress, Peer> peers = new();

        public void AddOrUpdatePeer(IPAddress ip, string username, string publicKeyXml)
        {
            peers[ip] = new Peer { IP = ip, Username = username, PublicKeyXml = publicKeyXml };
        }

        public Peer GetPeer(IPAddress ip) => peers.TryGetValue(ip, out var peer) ? peer : null;
        public List<Peer> GetAllPeers() => peers.Values.ToList();
    }

}
