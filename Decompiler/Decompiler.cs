using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;
using Decompiler.Transforms;

namespace Decompiler;

/// <summary>
/// The main decompiler class.
/// </summary>
public class Decompiler
{
    /// <summary>
    /// Max allowed count of instructions (-1 for no limit).
    /// </summary>
    public int MaxInstructionCount = 8000;

    /// <summary>
    /// All transforms applied to methods.
    /// </summary>
    public List<ITransform> Transforms =
    [
        new RemoveUnreachableBlocks(),
        new StackAnalyzer { MaxBlockVisitCount = 8000 },
        new BuildUseDefLists(),
        new BuildSsaForm(),
        new CreateLocals(),
        new BuildUseDefLists(),
        new RemoveUnusedLocalsAndInline(),
        new TypePropagation { MaxLoopCount = 8000 },
        new BuildUseDefLists()
    ];

    /// <summary>
    /// Decompiles a single method to .NET's IL (CIL), there's no return value because this sets the body.
    /// </summary>
    /// <param name="method">The method.</param>
    public void Decompile(Method method)
    {
        if (MaxInstructionCount != -1 && method.Instructions.Count > MaxInstructionCount)
            throw new LimitReachedException($"Too many instructions in {method.Definition.DeclaringType!.Name}.{method.Definition.Name}! ({method.Instructions.Count})");

        var definition = method.Definition;

        foreach (var transform in Transforms)
            transform.Apply(method);
    }

    /// <summary>
    /// Replaces the method body with 'throw new Exception(exceptionText);'.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="exceptionText">The exception text.</param>
    public static void ReplaceBodyWithException(MethodDefinition method, string exceptionText)
    {
        var importer = method.Module!.DefaultImporter;

        // get mscorlib
        var mscorlibReference = method.Module.AssemblyReferences.First(a => a.Name == "mscorlib");
        var mscorlib = mscorlibReference.Resolve()!.Modules[0];

        // get exception constructor
        var exception = mscorlib.TopLevelTypes.First(t => t.FullName == "System.Exception");
        var exceptionConstructor = exception.Methods.First(m =>
            m.Name == ".ctor" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.String");

        // add instructions
        method.CilMethodBody = new CilMethodBody(method);
        var instructions = method.CilMethodBody.Instructions;

        instructions.Add(CilOpCodes.Ldstr, exceptionText);
        instructions.Add(CilOpCodes.Newobj, importer.ImportMethod(exceptionConstructor));
        instructions.Add(CilOpCodes.Throw);
    }
}
