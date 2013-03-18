using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lorg
{
    public sealed class NameValueCollectionDictionary : IDictionary<string, string>
    {
        readonly System.Collections.Specialized.NameValueCollection coll;

        public NameValueCollectionDictionary(System.Collections.Specialized.NameValueCollection coll)
        {
            this.coll = coll;
        }

        public void Add(string key, string value)
        {
            coll.Add(key, value);
        }

        public bool ContainsKey(string key)
        {
            return coll.Get(key) != null;
        }

        public ICollection<string> Keys
        {
            get { return coll.AllKeys; }
        }

        public bool Remove(string key)
        {
            coll.Remove(key);
            return true;
        }

        public bool TryGetValue(string key, out string value)
        {
            value = coll.Get(key);
            if (value == null) return false;
            return true;
        }

        public ICollection<string> Values
        {
            get
            {
                var tmp = new List<string>(coll.Count);
                tmp.AddRange(coll.Cast<string>());
                return tmp;
            }
        }

        public string this[string key]
        {
            get
            {
                return coll[key];
            }
            set
            {
                coll[key] = value;
            }
        }

        public void Add(KeyValuePair<string, string> item)
        {
            coll.Add(item.Key, item.Value);
        }

        public void Clear()
        {
            coll.Clear();
        }

        public bool Contains(KeyValuePair<string, string> item)
        {
            return ContainsKey(item.Key);
        }

        public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public int Count
        {
            get { return coll.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public bool Remove(KeyValuePair<string, string> item)
        {
            return Remove(item.Key);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            for (int i = 0; i < coll.Count; ++i)
            {
                yield return new KeyValuePair<string, string>(coll.GetKey(i), coll.Get(i));
            }
            yield break;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
