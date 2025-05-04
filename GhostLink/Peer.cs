using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace GhostLink
{
    public class Peer
    {
        public IPAddress IP { get; set; }
        public string Username { get; set; }
        public string PublicKeyXml { get; set; }

        public override string ToString() => $"{Username} ({IP})";
    }

}
