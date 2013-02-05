using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public struct HashedLogIdentifier : ILogIdentifier
    {
        readonly byte[] _sha1hash;
        readonly int _instanceID;

        public HashedLogIdentifier(byte[] sha1hash, int instanceID)
        {
            _sha1hash = sha1hash;
            _instanceID = instanceID;
        }

        public int InstanceID { get { return _instanceID; } }

        public string GetEndUserFormat()
        {
            return "E:n{0}:x{1}".F(
                _instanceID,
                _sha1hash.ToHexString(12)
            );
        }
    }
}
