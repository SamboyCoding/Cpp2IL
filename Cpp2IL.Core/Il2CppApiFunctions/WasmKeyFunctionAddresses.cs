using System.Collections.Generic;

namespace Cpp2IL.Core.Il2CppApiFunctions;

public class WasmKeyFunctionAddresses : BaseKeyFunctionAddresses
{
    protected override ulong GetObjectIsInstFromSystemType()
    {
        return 0;
    }

    protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
    {
        yield break;
    }

    protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
    {
        return 0;
    }

    protected override int GetCallerCount(ulong toWhere)
    {
        return 0;
    }
}