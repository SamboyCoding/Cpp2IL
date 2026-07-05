using System.Linq;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Tests;

public class TypeAnalysisContextTests
{
    [Test]
    public void InterfacesHaveNoBaseType()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        using (Assert.EnterMultipleScope())
        {
            var count = 0;
            foreach (var assembly in appContext.Assemblies)
            {
                foreach (var type in assembly.Types)
                {
                    if (!type.IsInterface)
                        continue;

                    Assert.That(type.DefaultBaseType, Is.Null);
                    Assert.That(type.BaseType, Is.Null);
                    count++;
                }
            }
            Assert.That(count, Is.GreaterThan(0));
        }
    }

    [Test]
    public void StaticClassesHaveObjectBaseType()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        using (Assert.EnterMultipleScope())
        {
            var count = 0;
            foreach (var assembly in appContext.Assemblies)
            {
                foreach (var type in assembly.Types)
                {
                    if (!type.IsStatic)
                        continue;

                    Assert.That(type.DefaultBaseType, Is.EqualTo(appContext.SystemTypes.SystemObjectType));
                    Assert.That(type.BaseType, Is.EqualTo(appContext.SystemTypes.SystemObjectType));
                    count++;
                }
            }
            Assert.That(count, Is.GreaterThan(0));
        }
    }

    [Test]
    public void GenericInstanceTypeHasInstantiatedBaseType()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        // ObjectComparer<T> inherits from Comparer<T>
        var objectComparerType = appContext.SystemTypes.SystemObjectType.DeclaringAssembly.GetTypeByFullName("System.Collections.Generic.ObjectComparer`1");

        Assert.That(objectComparerType, Is.Not.Null);
        Assert.That(objectComparerType.BaseType, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(objectComparerType.GenericParameters, Has.Count.EqualTo(1));
            Assert.That(objectComparerType.BaseType, Is.InstanceOf<GenericInstanceTypeAnalysisContext>());
        }
        Assert.That(((GenericInstanceTypeAnalysisContext)objectComparerType.BaseType).GenericArguments[0], Is.EqualTo(objectComparerType.GenericParameters[0]));

        var objectComparerStringType = objectComparerType.MakeGenericInstanceType(appContext.SystemTypes.SystemStringType);
        Assert.That(objectComparerStringType.BaseType, Is.Not.Null);
        Assert.That(objectComparerStringType.BaseType, Is.InstanceOf<GenericInstanceTypeAnalysisContext>());

        var baseType = (GenericInstanceTypeAnalysisContext)objectComparerStringType.BaseType;
        Assert.That(baseType.GenericArguments, Has.Count.EqualTo(1));
        Assert.That(baseType.GenericArguments[0], Is.EqualTo(appContext.SystemTypes.SystemStringType));
    }

    [Test]
    public void SelfReferencingGenericInstanceTypeHasInstantiatedInterfaces()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        // NativeArray<T> implements IEquatable<NativeArray<T>>
        var iequatableType = appContext.SystemTypes.SystemObjectType.DeclaringAssembly.GetTypeByFullName("System.IEquatable`1");
        var nativeArrayType = appContext.AssembliesByName["UnityEngine.CoreModule"].GetTypeByFullName("Unity.Collections.NativeArray`1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(iequatableType, Is.Not.Null);
            Assert.That(nativeArrayType, Is.Not.Null);
        }

        var nativeByteArrayType = nativeArrayType.MakeGenericInstanceType(appContext.SystemTypes.SystemByteType);
        var implementedInterface = nativeByteArrayType.InterfaceContexts.OfType<GenericInstanceTypeAnalysisContext>().FirstOrDefault(i => i.GenericType == iequatableType);
        Assert.That(implementedInterface, Is.Not.Null);
        Assert.That(implementedInterface.GenericArguments, Has.Count.EqualTo(1));
        Assert.That(implementedInterface.GenericArguments[0], Is.EqualTo(nativeByteArrayType));
    }
}
