using System.Collections.Generic;

namespace Cpp2IL.Core.Il2CppApiFunctions;

//stub, pretty much
public class ArmV7KeyFunctionAddresses : BaseKeyFunctionAddresses
{
    protected override ulong GetObjectIsInstFromSystemType() => 0;

    protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore) => [];

    protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false) => 0;

    protected override int GetCallerCount(ulong toWhere) => 0;
}
