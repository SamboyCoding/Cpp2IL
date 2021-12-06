using System.Collections.Generic;
using LibCpp2IL;

namespace Cpp2IL.Core.Model;

public class TokenComparer : IComparer<IIl2CppTokenProvider>
{
    public int Compare(IIl2CppTokenProvider x, IIl2CppTokenProvider y) => ReferenceEquals(x, y) ? 0 : x.Token.CompareTo(y.Token);
}