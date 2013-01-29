using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public static class Hash
    {
        public static byte[] SHA1(string value)
        {
            using (var sha1 = System.Security.Cryptography.SHA1Managed.Create())
                return sha1.ComputeHash(new UTF8Encoding(false).GetBytes(value));
        }
    }
}
