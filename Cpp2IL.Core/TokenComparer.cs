using System.Collections.Generic;
using LibCpp2IL;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core
{
    public class TokenComparer : IComparer<IIl2CppTokenProvider>
    {
        public int Compare(IIl2CppTokenProvider x, IIl2CppTokenProvider y)
        {
            if (ReferenceEquals(x, y)) return 0;
            return x.Token.CompareTo(y.Token);
        }
    }
}