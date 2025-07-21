using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ProcessingLayers;

public class NativeMethodDetectionProcessingLayer : Cpp2IlProcessingLayer
{
    public override string Name => "Native Method Detection";

    public override string Id => "nativemethoddetector";

    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        var nativeMethodInfoStack = new Stack<(ulong, bool)>();
        var cppNativeMethodsType = appContext.AssembliesByName["mscorlib"].InjectType(
            "Cpp2ILInjected",
            "CppNativeMethods",
            null,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed); //public static class
        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            foreach (var m in assemblyAnalysisContext.Types.SelectMany(t => t.Methods))
            {
                AnalyzeMethod(appContext, m, nativeMethodInfoStack);
            }

            if (Cpp2IlApi.LowMemoryMode)
                GC.Collect();
        }

        if (Cpp2IlApi.LowMemoryMode)
            GC.Collect();

        while (nativeMethodInfoStack.Count > 0)
        {
            (var address, var isVoid) = nativeMethodInfoStack.Pop();
            if (!appContext.MethodsByAddress.ContainsKey(address))
            {
                var m = new NativeMethodAnalysisContext(cppNativeMethodsType, address, isVoid);
                cppNativeMethodsType.Methods.Add(m);
                appContext.MethodsByAddress.Add(address, [m]);
                AnalyzeMethod(appContext, m, nativeMethodInfoStack);
            }
        }
    }

    private static void AnalyzeMethod(ApplicationAnalysisContext appContext, MethodAnalysisContext m, Stack<(ulong, bool)> nativeMethodInfoStack)
    {
        if (m.UnderlyingPointer == 0)
            return;

        var convertedIsil = appContext.InstructionSet.GetIsilFromMethod(m);

        if (convertedIsil is { Count: 0 })
        {
            return;
        }

        foreach (var instruction in convertedIsil)
        {
            if (instruction.OpCode == OpCode.Call)
            {
                if (TryGetAddressFromInstruction(instruction, out var address) && !appContext.MethodsByAddress.ContainsKey(address))
                {
                    nativeMethodInfoStack.Push((address, true));
                }
            }
            else if (instruction.OpCode == OpCode.CallVoid)
            {
                if (TryGetAddressFromInstruction(instruction, out var address) && !appContext.MethodsByAddress.ContainsKey(address))
                {
                    nativeMethodInfoStack.Push((address, m.IsVoid));
                }
            }
        }
    }

    private static bool TryGetAddressFromInstruction(Instruction instruction, out ulong address)
    {
        var operand = instruction.Operands[0];

        if (instruction.Operands.Count > 0 && instruction.Operands[0].IsNumeric())
        {
            address = (ulong)operand;
            return true;
        }

        address = default;
        return false;
    }
}
