using System.Linq;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Model.CustomAttributes;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Tests;

public class V29CustomAttributeTests
{
    [SetUp]
    public void Setup()
    {
        Cpp2IlApi.ResetInternalState();
        TestGameLoader.LoadSimple2022Game();
    }

    [Test]
    public void TestSimpleParameterlessV29Attribute()
    {
        var context = Cpp2IlApi.CurrentAppContext!;
        var type = context.GetAssemblyByName("mscorlib")!.GetTypeByFullName("System.Int32")!;
        
        Assert.DoesNotThrow(() => type.AnalyzeCustomAttributeData());
        
        var serializableAttribute = type.CustomAttributes!.FirstOrDefault(ca => ca.Constructor.DeclaringType!.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        Assert.That(serializableAttribute, Is.Not.Null);
    }

    [Test]
    public void TestV29AttributeWithStringParam()
    {
        var context = Cpp2IlApi.CurrentAppContext!;
        var type = context.GetAssemblyByName("mscorlib")!.GetTypeByFullName("System.Collections.Generic.List`1")!;
        
        Assert.DoesNotThrow(() => type.AnalyzeCustomAttributeData());
        
        var debuggerDisplayAttribute = type.CustomAttributes!.FirstOrDefault(ca => ca.Constructor.DeclaringType!.FullName == "System.Diagnostics.DebuggerDisplayAttribute");
        Assert.That(debuggerDisplayAttribute, Is.Not.Null);
        Assert.That(debuggerDisplayAttribute!.ConstructorParameters, Has.Count.EqualTo(1));
        Assert.That(debuggerDisplayAttribute!.ConstructorParameters![0], Is.InstanceOf<CustomAttributePrimitiveParameter>().And.Matches<CustomAttributePrimitiveParameter>(p => p.PrimitiveValue is "Count = {Count}"));
    }

    [Test]
    public void TestV29AttributeWithTypeParam()
    {
        var context = Cpp2IlApi.CurrentAppContext!;
        var type = context.GetAssemblyByName("mscorlib")!.GetTypeByFullName("System.Collections.Generic.List`1")!;
        
        Assert.DoesNotThrow(() => type.AnalyzeCustomAttributeData());
        
        var debuggerTypeProxyAttribute = type.CustomAttributes!.FirstOrDefault(ca => ca.Constructor.DeclaringType!.FullName == "System.Diagnostics.DebuggerTypeProxyAttribute");
        Assert.That(debuggerTypeProxyAttribute, Is.Not.Null);
        Assert.That(debuggerTypeProxyAttribute!.ConstructorParameters, Has.Count.EqualTo(1));
        Assert.That(debuggerTypeProxyAttribute!.ConstructorParameters![0], Is.InstanceOf<CustomAttributeTypeParameter>().And.Property("TypeContext").Property("FullName").EqualTo("System.Collections.Generic.ICollectionDebugView`1"));
    }

    [Test]
    public void TestV29AttributeWithEnumParam()
    {
        var context = Cpp2IlApi.CurrentAppContext!;
        var type = context.GetAssemblyByName("mscorlib")!.GetTypeByFullName("System.Array")!;
        var property = type.Properties!.First(p => p.Name == "Length");
        
        Assert.DoesNotThrow(() => property.Getter!.AnalyzeCustomAttributeData());
        
        var reliabilityContractAttribute = property.Getter!.CustomAttributes!.FirstOrDefault(ca => ca.Constructor.DeclaringType!.FullName == "System.Runtime.ConstrainedExecution.ReliabilityContractAttribute");
        
        Assert.That(reliabilityContractAttribute, Is.Not.Null);
        Assert.That(reliabilityContractAttribute!.ConstructorParameters, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(reliabilityContractAttribute!.ConstructorParameters![0], Is.InstanceOf<CustomAttributeEnumParameter>().And.Property("EnumTypeContext").Property("FullName").EqualTo("System.Runtime.ConstrainedExecution.Consistency"));
            Assert.That(reliabilityContractAttribute!.ConstructorParameters![0], Is.InstanceOf<CustomAttributeEnumParameter>().And.Matches<CustomAttributeEnumParameter>(p => p.UnderlyingPrimitiveParameter is {PrimitiveValue: 3 /* Consistency.WillNotCorruptState */}));
            Assert.That(reliabilityContractAttribute!.ConstructorParameters![1], Is.InstanceOf<CustomAttributeEnumParameter>().And.Property("EnumTypeContext").Property("FullName").EqualTo("System.Runtime.ConstrainedExecution.Cer"));
            Assert.That(reliabilityContractAttribute!.ConstructorParameters![1], Is.InstanceOf<CustomAttributeEnumParameter>().And.Matches<CustomAttributeEnumParameter>(p => p.UnderlyingPrimitiveParameter is {PrimitiveValue: 2 /* Cer.Success */}));
        });
    }

    [Test]
    public void TestV29AttributeWithStringArray()
    {
        var context = Cpp2IlApi.CurrentAppContext!;
        var type = context.GetAssemblyByName("UnityEngine.IMGUIModule")!.GetTypeByFullName("UnityEngine.GUISkin")!;
        
        Assert.DoesNotThrow(() => type.AnalyzeCustomAttributeData());
        
        var assetFileNameExtensionAttribute = type.CustomAttributes!.FirstOrDefault(ca => ca.Constructor.DeclaringType!.FullName == "UnityEngine.AssetFileNameExtensionAttribute");
        
        Assert.That(assetFileNameExtensionAttribute, Is.Not.Null);
        Assert.That(assetFileNameExtensionAttribute!.ConstructorParameters, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(assetFileNameExtensionAttribute!.ConstructorParameters![0], Is.InstanceOf<CustomAttributePrimitiveParameter>().And.Matches<CustomAttributePrimitiveParameter>(p => p.PrimitiveValue is "guiskin"));
            Assert.That(assetFileNameExtensionAttribute!.ConstructorParameters![1], Is.InstanceOf<CustomAttributeArrayParameter>().And.Matches<CustomAttributeArrayParameter>(p => p.ArrayElements.Count == 0 && p.ArrType == Il2CppTypeEnum.IL2CPP_TYPE_STRING));
        });
    }

    [Test]
    public void TestV29AttributeWithProperty()
    {
        var context = Cpp2IlApi.CurrentAppContext!;
        var type = context.GetAssemblyByName("mscorlib")!.GetTypeByFullName("System.Runtime.ConstrainedExecution.ReliabilityContractAttribute")!;
        
        Assert.DoesNotThrow(() => type.AnalyzeCustomAttributeData());
        
        var attributeUsageAttribute = type.CustomAttributes!.FirstOrDefault(ca => ca.Constructor.DeclaringType!.FullName == "System.AttributeUsageAttribute");
        
        Assert.That(attributeUsageAttribute, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(attributeUsageAttribute!.ConstructorParameters, Has.Count.EqualTo(1)); //But we don't care to check the value of this parameter, just that it's there.
            Assert.That(attributeUsageAttribute!.Properties, Has.Count.EqualTo(1));
        });

        var firstProp = attributeUsageAttribute!.Properties![0];
        Assert.That(firstProp, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(firstProp.Property, Is.InstanceOf<PropertyAnalysisContext>().And.Property("Name").EqualTo("Inherited"));
            Assert.That(firstProp.Value, Is.InstanceOf<CustomAttributePrimitiveParameter>().And.Matches<CustomAttributePrimitiveParameter>(p => p.PrimitiveValue is false));
        });
    }
}
