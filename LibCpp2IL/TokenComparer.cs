using System.Collections.Generic;

namespace LibCpp2IL;

public class TokenComparer : IComparer<IIl2CppTokenProvider>
{
    public int Compare(IIl2CppTokenProvider? x, IIl2CppTokenProvider? y) =>
        ReferenceEquals(x, y) ? 0
        : ReferenceEquals(x, null) ? 0
        : ReferenceEquals(y, null) ? 0 : x.Token.CompareTo(y.Token);
}
