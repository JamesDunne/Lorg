using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lorg
{
    public struct Maybe<T>
    {
        readonly bool hasValue;
        readonly T value;

        public bool HasValue { get { return hasValue; } }
        public T Value
        {
            get
            {
                if (!hasValue) throw new NullReferenceException();
                return value;
            }
        }

        internal Maybe(T newValue)
        {
            hasValue = true;
            value = newValue;
        }

        public static readonly Maybe<T> Nothing = new Maybe<T>();

        public static implicit operator Maybe<T>(T newValue)
        {
            return new Maybe<T>(newValue);
        }
    }

    public static class Maybe
    {
        public static Maybe<T> Just<T>(T value)
        {
            return new Maybe<T>(value);
        }
    }
}

