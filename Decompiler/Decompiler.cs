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
    /// All transforms applied to methods.
    /// </summary>
    public List<ITransform> Transforms =
    [
        new RemoveUnreachableBlocks(),
        new StackAnalyzer()
    ];

    /// <summary>
    /// Decompiles a single method to .NET's IL (CIL), there's no return value because this sets the body.
    /// </summary>
    /// <param name="method">The method.</param>
    public void Decompile(Method method)
    {
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
