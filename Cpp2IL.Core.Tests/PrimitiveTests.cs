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

        var notMscorlib = assemblies.First(a => a.Name != "mscorlib").ManifestModule!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(appContext.SystemTypes.SystemByteType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemSByteType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemInt16Type.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemUInt16Type.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemInt32Type.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemUInt32Type.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemInt64Type.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemUInt64Type.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemSingleType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemDoubleType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemIntPtrType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemUIntPtrType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemBooleanType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemCharType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemStringType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemObjectType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });
            Assert.That(appContext.SystemTypes.SystemTypedReferenceType.ToTypeSignature() is CorLibTypeSignature { Scope: ModuleDefinition });

            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemByteType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemSByteType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemInt16Type.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemUInt16Type.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemInt32Type.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemUInt32Type.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemInt64Type.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemUInt64Type.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemSingleType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemDoubleType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemIntPtrType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemUIntPtrType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemBooleanType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemCharType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemStringType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemObjectType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
            Assert.That(notMscorlib.DefaultImporter.ImportTypeSignature(appContext.SystemTypes.SystemTypedReferenceType.ToTypeSignature()) is CorLibTypeSignature { Scope: AssemblyReference });
        }
    }
}
