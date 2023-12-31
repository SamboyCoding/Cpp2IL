using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Api;
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
        var cppNativeMethodsType = appContext.AssembliesByName["mscorlib"].InjectType("Cpp2ILInjected", "CppNativeMethods", null);
        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            foreach (var m in assemblyAnalysisContext.Types.SelectMany(t => t.Methods))
            {
                AnalyzeMethod(appContext, m, nativeMethodInfoStack);
            }
        }
        while (nativeMethodInfoStack.Count > 0)
        {
            (var address, var isVoid) = nativeMethodInfoStack.Pop();
            if (!appContext.MethodsByAddress.ContainsKey(address))
            {
                var m = new NativeMethodAnalysisContext(cppNativeMethodsType, address, isVoid);
                cppNativeMethodsType.Methods.Add(m);
                m.InjectedReturnType = isVoid ? appContext.SystemTypes.SystemVoidType : appContext.SystemTypes.SystemObjectType;
                appContext.MethodsByAddress.Add(address, new(1) { m });
                AnalyzeMethod(appContext, m, nativeMethodInfoStack);
            }
        }
    }

    private static void AnalyzeMethod(ApplicationAnalysisContext appContext, MethodAnalysisContext m, Stack<(ulong, bool)> nativeMethodInfoStack)
    {
        if (m.UnderlyingPointer == 0)
            return;

        try
        {
            m.Analyze();
        }
        catch (Exception ex)
        {
#if DEBUG
            if (ex.Message is not "Failed to convert to ISIL. Reason: Jump target not found in method. Ruh roh" && !ex.Message.StartsWith("Instruction "))
            {
            }
#endif
            m.ConvertedIsil = null;
            return;
        }

        if (m.ConvertedIsil is { Count: 0 })
        {
            return;
        }

        foreach (var instruction in m.ConvertedIsil)
        {
            if (instruction.OpCode == InstructionSetIndependentOpCode.Call)
            {
                if (TryGetAddressFromInstruction(instruction, out var address) && !appContext.MethodsByAddress.ContainsKey(address))
                {
                    nativeMethodInfoStack.Push((address, true));
                }
            }
            else if (instruction.OpCode == InstructionSetIndependentOpCode.CallNoReturn)
            {
                if (TryGetAddressFromInstruction(instruction, out var address) && !appContext.MethodsByAddress.ContainsKey(address))
                {
                    nativeMethodInfoStack.Push((address, m.IsVoid));
                }
            }
        }
    }

    private static bool TryGetAddressFromInstruction(InstructionSetIndependentInstruction instruction, out ulong address)
    {
        if (instruction.Operands.Length > 0 && instruction.Operands[0].Data is IsilImmediateOperand operand)
        {
            address = operand.Value.ToUInt64(null);
            return true;
        }
        address = default;
        return false;
    }
}
