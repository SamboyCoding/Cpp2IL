using System.Linq;
using System.Reflection;

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

        var methodsByAssembly = injectedType.InjectMethodToAllAssemblies("TestZeroArgMethod", appContext.SystemTypes.SystemVoidType, MethodAttributes.Public);
        
        Assert.That(methodsByAssembly, Has.Count.EqualTo(appContext.Assemblies.Count));
        Assert.That(methodsByAssembly.Values.First(), Has.Property("Name").EqualTo("TestZeroArgMethod").And.Property("ReturnType").EqualTo(appContext.SystemTypes.SystemVoidType));
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
        Assert.That(constructorsByAssembly.Values.First(), Has.Property("Name").EqualTo(".ctor").And.Property("ReturnType").EqualTo(appContext.SystemTypes.SystemVoidType));
        Assert.That(constructorsByAssembly.Values.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithConstructors"));
    }
    
    [Test]
    public void TestMethodWithParametersInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;
        
        var baseType = appContext!.SystemTypes.SystemObjectType;
        var injectedType = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TestInjectedTypeWithMethodsWithParameters", baseType);

        var methodsByAssembly = injectedType.InjectMethodToAllAssemblies("TestMethodWithParameters", appContext.SystemTypes.SystemVoidType, MethodAttributes.Public, appContext.SystemTypes.SystemInt32Type, appContext.SystemTypes.SystemStringType);
        
        Assert.That(methodsByAssembly, Has.Count.EqualTo(appContext.Assemblies.Count));
        Assert.That(methodsByAssembly.Values.First(), Has.Property("Name").EqualTo("TestMethodWithParameters").And.Property("ReturnType").EqualTo(appContext.SystemTypes.SystemVoidType));
        Assert.Multiple(() =>
        {
            Assert.That(methodsByAssembly.Values.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithMethodsWithParameters"));
            Assert.That(methodsByAssembly.Values.First().Parameters, Has.Count.EqualTo(2));
        });
        Assert.Multiple(() =>
        {
            Assert.That(methodsByAssembly.Values.First().Parameters[0], Has.Property("ParameterType").EqualTo(appContext.SystemTypes.SystemInt32Type));
            Assert.That(methodsByAssembly.Values.First().Parameters[1], Has.Property("ParameterType").EqualTo(appContext.SystemTypes.SystemStringType));
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
        Assert.That(fieldsByAssembly.Values.First(), Has.Property("Name").EqualTo("TestField").And.Property("FieldType").EqualTo(appContext.SystemTypes.SystemInt32Type));
        Assert.That(fieldsByAssembly.Values.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithFields"));
    }

    [Test]
    public void TestPropertyInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;
        
        var baseType = appContext!.SystemTypes.SystemObjectType;
        var injectedType = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TestInjectedTypeWithProperties", baseType);
        var gettersByAssembly = injectedType.InjectMethodToAllAssemblies("get_TestProperty", appContext.SystemTypes.SystemInt32Type, MethodAttributes.Public);
        var propertiesByAssembly = injectedType.InjectPropertyToAllAssemblies("TestProperty", appContext.SystemTypes.SystemInt32Type, gettersByAssembly, null, PropertyAttributes.None);

        Assert.That(propertiesByAssembly, Has.Count.EqualTo(appContext.Assemblies.Count));
        Assert.That(propertiesByAssembly.Values.First(), Has.Property("Name").EqualTo("TestProperty").And.Property("PropertyType").EqualTo(appContext.SystemTypes.SystemInt32Type));
        Assert.That(propertiesByAssembly.Values.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithProperties"));
    }

    [Test]
    public void TestEventInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;

        var baseType = appContext!.SystemTypes.SystemObjectType;
        var injectedType = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TestInjectedTypeWithEvents", baseType);
        var addersByAssembly = injectedType.InjectMethodToAllAssemblies("add_TestEvent", appContext.SystemTypes.SystemInt32Type, MethodAttributes.Public);
        var removersByAssembly = injectedType.InjectMethodToAllAssemblies("remove_TestEvent", appContext.SystemTypes.SystemInt32Type, MethodAttributes.Public);
        var eventsByAssembly = injectedType.InjectEventToAllAssemblies("TestEvent", appContext.SystemTypes.SystemInt32Type, addersByAssembly, removersByAssembly, null, EventAttributes.None);

        Assert.That(eventsByAssembly, Has.Count.EqualTo(appContext.Assemblies.Count));
        Assert.That(eventsByAssembly.Values.First(), Has.Property("Name").EqualTo("TestEvent").And.Property("EventType").EqualTo(appContext.SystemTypes.SystemInt32Type));
        Assert.That(eventsByAssembly.Values.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithEvents"));
    }

    [Test]
    public void TestNestedTypeInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;

        var baseType = appContext!.SystemTypes.SystemObjectType;
        var declaringType = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TestInjectedTypeWithEvents", baseType);
        var injectedType = declaringType.InjectNestedType("NestedType", baseType);

        Assert.That(injectedType.InjectedTypes, Has.Length.EqualTo(appContext.Assemblies.Count));
        Assert.That(injectedType.InjectedTypes.First(), Has.Property("Name").EqualTo("NestedType").And.Property("Namespace").EqualTo("").And.Property("BaseType").EqualTo(appContext.SystemTypes.SystemObjectType));
        Assert.That(injectedType.InjectedTypes.First().DeclaringType, Has.Property("FullName").EqualTo("Cpp2ILInjected.TestInjectedTypeWithEvents"));
    }

    [Test]
    public void TestNestedTypeInjectionSingle()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;

        var declaringType = appContext!.SystemTypes.SystemObjectType;
        var injectedType = declaringType.InjectNestedType("NestedType", null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(appContext.AllTypes, Contains.Item(injectedType));
            Assert.That(declaringType.NestedTypes, Contains.Item(injectedType));
        }
    }

    [Test]
    public void TestAssemblyInjection()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;

        var assemblyCount = appContext!.Assemblies.Count;
        var injectedAssembly = appContext.InjectAssembly("TestInjectedAssembly");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(appContext.Assemblies, Has.Count.EqualTo(assemblyCount + 1));
            Assert.That(appContext.Assemblies, Contains.Item(injectedAssembly));
        }
    }
}
