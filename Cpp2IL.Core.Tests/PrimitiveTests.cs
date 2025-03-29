using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.Utils.AsmResolver;

namespace Cpp2IL.Core.Tests;

public class PrimitiveTests
{
    [Test]
    public void PrimitiveTypesAreCorLibTypeSignature()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        var assemblies = new AsmResolverDllOutputFormatEmpty().BuildAssemblies(appContext);

        var mscorlib = assemblies.First(a => a.Name == "mscorlib").ManifestModule!;
        var notMscorlib = assemblies.First(a => a.Name != "mscorlib").ManifestModule!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(appContext.SystemTypes.SystemByteType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemSByteType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemInt16Type.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemUInt16Type.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemInt32Type.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemUInt32Type.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemInt64Type.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemUInt64Type.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemSingleType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemDoubleType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemIntPtrType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemUIntPtrType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemBooleanType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemCharType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemStringType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemObjectType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemTypedReferenceType.ToTypeSignature(mscorlib) is CorLibTypeSignature { Scope: ModuleDefinition });

            Assert.That(appContext.SystemTypes.SystemByteType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemSByteType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemInt16Type.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemUInt16Type.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemInt32Type.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemUInt32Type.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemInt64Type.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemUInt64Type.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemSingleType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemDoubleType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemIntPtrType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemUIntPtrType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemBooleanType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemCharType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemStringType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemObjectType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(appContext.SystemTypes.SystemTypedReferenceType.ToTypeSignature(notMscorlib) is CorLibTypeSignature { Scope: AssemblyReference });
        }
    }
}
