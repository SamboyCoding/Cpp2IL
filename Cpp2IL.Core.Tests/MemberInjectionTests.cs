using System.Linq;
using System.Reflection;
using AssetRipper.Primitives;

namespace Cpp2IL.Core.Tests;

public class MemberInjectionTests
{
    [SetUp]
    public void Setup()
    {
        Cpp2IlApi.ResetInternalState();
        TestGameLoader.LoadSimple2019Game();
    }

    [Test]
    public void TestTypeInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;
        
        var baseType = appContext!.SystemTypes.SystemObjectType;
        var injectedType = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TestInjectedType", baseType);

        Assert.That(injectedType.InjectedTypes, Has.Length.EqualTo(appContext.Assemblies.Count));
    }

    [Test]
    public void TestZeroArgumentMethodInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;
        
        var baseType = appContext!.SystemTypes.SystemObjectType;
        var injectedType = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TestInjectedTypeWithMethods", baseType);

        var methodsByAssembly = injectedType.InjectMethodToAllAssemblies("TestZeroArgMethod", false, appContext.SystemTypes.SystemVoidType, MethodAttributes.Public);
        
        Assert.That(methodsByAssembly, Has.Count.EqualTo(appContext.Assemblies.Count));
        Assert.That(methodsByAssembly.Values.First(), Has.Property("Name").EqualTo("TestZeroArgMethod").And.Property("ReturnTypeContext").EqualTo(appContext.SystemTypes.SystemVoidType));
        Assert.That(methodsByAssembly.Values.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithMethods"));
    }
    
    [Test]
    public void TestConstructorInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;
        
        var baseType = appContext!.SystemTypes.SystemObjectType;
        var injectedType = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TestInjectedTypeWithConstructors", baseType);

        var constructorsByAssembly = injectedType.InjectConstructor(false);
        
        Assert.That(constructorsByAssembly, Has.Count.EqualTo(appContext.Assemblies.Count));
        Assert.That(constructorsByAssembly.Values.First(), Has.Property("Name").EqualTo(".ctor").And.Property("ReturnTypeContext").EqualTo(appContext.SystemTypes.SystemVoidType));
        Assert.That(constructorsByAssembly.Values.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithConstructors"));
    }
    
    [Test]
    public void TestMethodWithParametersInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;
        
        var baseType = appContext!.SystemTypes.SystemObjectType;
        var injectedType = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TestInjectedTypeWithMethodsWithParameters", baseType);

        var methodsByAssembly = injectedType.InjectMethodToAllAssemblies("TestMethodWithParameters", false, appContext.SystemTypes.SystemVoidType, MethodAttributes.Public, appContext.SystemTypes.SystemInt32Type, appContext.SystemTypes.SystemStringType);
        
        Assert.That(methodsByAssembly, Has.Count.EqualTo(appContext.Assemblies.Count));
        Assert.That(methodsByAssembly.Values.First(), Has.Property("Name").EqualTo("TestMethodWithParameters").And.Property("ReturnTypeContext").EqualTo(appContext.SystemTypes.SystemVoidType));
        Assert.Multiple(() =>
        {
            Assert.That(methodsByAssembly.Values.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithMethodsWithParameters"));
            Assert.That(methodsByAssembly.Values.First().Parameters, Has.Count.EqualTo(2));
        });
        Assert.Multiple(() =>
        {
            Assert.That(methodsByAssembly.Values.First().Parameters[0], Has.Property("ParameterTypeContext").EqualTo(appContext.SystemTypes.SystemInt32Type));
            Assert.That(methodsByAssembly.Values.First().Parameters[1], Has.Property("ParameterTypeContext").EqualTo(appContext.SystemTypes.SystemStringType));
        });

        Assert.DoesNotThrow(() => methodsByAssembly.Values.First().Parameters.Select(p => p.Name).ToList());
    }
    
    [Test]
    public void TestFieldInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;
        
        var baseType = appContext!.SystemTypes.SystemObjectType;
        var injectedType = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TestInjectedTypeWithFields", baseType);

        var fieldsByAssembly = injectedType.InjectFieldToAllAssemblies("TestField", appContext.SystemTypes.SystemInt32Type, FieldAttributes.Public);
        
        Assert.That(fieldsByAssembly, Has.Count.EqualTo(appContext.Assemblies.Count));
        Assert.That(fieldsByAssembly.Values.First(), Has.Property("Name").EqualTo("TestField").And.Property("FieldTypeContext").EqualTo(appContext.SystemTypes.SystemInt32Type));
        Assert.That(fieldsByAssembly.Values.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithFields"));
    }
}
