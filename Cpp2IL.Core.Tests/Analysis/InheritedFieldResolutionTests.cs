using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using NUnit.Framework;

namespace Cpp2IL.Core.Tests.Analysis;

public class InheritedFieldResolutionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ResolvesFieldDeclaredOnGenericBase(bool is32Bit)
    {
        var fixture = new Fixture(is32Bit);
        var (method, instruction, receiver) = fixture.Load(fixture.Derived, 3 * fixture.PointerSize);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.True);
        var field = (FieldReference)instruction.Operands[1];
        Assert.That(field.Local, Is.SameAs(receiver));
        Assert.That(field.Field.Name, Is.EqualTo("Identifier"));
        Assert.That(field.Field.DeclaringType, Is.SameAs(fixture.GenericBase));
        Assert.That(field.Field.FieldType, Is.SameAs(fixture.Text));
    }

    [Test]
    public void ResolvesStoreToGenericBaseField()
    {
        var fixture = new Fixture(false);
        var (method, instruction, _) = fixture.Load(fixture.Derived, 3 * fixture.PointerSize);
        var source = instruction.Operands[0];
        instruction.SetOperand(0, instruction.Operands[1]);
        instruction.SetOperand(1, source);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.True);
        Assert.That(((FieldReference)instruction.Operands[0]).Field.Name, Is.EqualTo("Identifier"));
        Assert.That(instruction.Operands[1], Is.SameAs(source));
    }

    [Test]
    public void PreservesGenericLayoutWithoutAncestorFields()
    {
        var fixture = new Fixture(false);
        fixture.Definition.OverrideBaseType = fixture.Type("EmptyBase", null);
        var (method, instruction, _) = fixture.Load(fixture.GenericBase, 2 * fixture.PointerSize);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.True);
        Assert.That(((FieldReference)instruction.Operands[1]).Field.Name, Is.EqualTo("Identifier"));
    }

    [Test]
    public void LeavesIndexedMemoryUnresolved()
    {
        var fixture = new Fixture(false);
        var (method, instruction, receiver) = fixture.Load(fixture.Derived, 3 * fixture.PointerSize);
        instruction.SetOperand(1, new MemoryOperand(receiver, new Register(null, "index"), 3 * fixture.PointerSize, 8));
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.False);
        Assert.That(instruction.Operands[1], Is.TypeOf<MemoryOperand>());
    }

    [Test]
    public void ResolvesAncestorThroughGenericReceiver()
    {
        var fixture = new Fixture(false);
        var (method, instruction, _) = fixture.Load(fixture.GenericBase, 2 * fixture.PointerSize);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.True);
        Assert.That(((FieldReference)instruction.Operands[1]).Field, Is.SameAs(fixture.NativeField));
    }

    [Test]
    public void SubstitutesGenericFieldTypeAndKeepsDeclaringInstance()
    {
        var fixture = new Fixture(false);
        fixture.Identifier.FieldType = fixture.Parameter;
        var (method, instruction, _) = fixture.Load(fixture.Derived, 3 * fixture.PointerSize);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.True);
        var field = ((FieldReference)instruction.Operands[1]).Field;
        Assert.That(field.FieldType, Is.SameAs(fixture.Text));
        Assert.That(field.DeclaringType, Is.SameAs(fixture.GenericBase));
    }

    [Test]
    public void WalksMultipleGenericBasesWithArgumentSubstitution()
    {
        var fixture = new Fixture(false);
        fixture.Identifier.FieldType = fixture.Parameter;
        var middle = fixture.Type("Middle", null);
        var parameter = new GenericParameterTypeAnalysisContext("T", 0, Il2CppTypeEnum.IL2CPP_TYPE_VAR, 0, middle);
        middle.GenericParameters.Add(parameter);
        middle.OverrideBaseType = fixture.Definition.MakeGenericInstanceType(parameter);
        fixture.Derived.OverrideBaseType = middle.MakeGenericInstanceType(fixture.Text);
        var (method, instruction, _) = fixture.Load(fixture.Derived, 3 * fixture.PointerSize);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.True);
        var field = ((FieldReference)instruction.Operands[1]).Field;
        Assert.That(field.FieldType, Is.SameAs(fixture.Text));
        Assert.That(((GenericInstanceTypeAnalysisContext)field.DeclaringType).GenericType, Is.SameAs(fixture.Definition));
    }

    [Test]
    public void PreservesOrdinaryDerivedFieldLookup()
    {
        var fixture = new Fixture(false);
        var own = fixture.Derived.InjectFieldContext("Own", fixture.Text, FieldAttributes.Public);
        own.Offset = 4 * fixture.PointerSize;
        var (method, instruction, _) = fixture.Load(fixture.Derived, own.Offset);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.True);
        Assert.That(((FieldReference)instruction.Operands[1]).Field, Is.SameAs(own));
    }

    [Test]
    public void DoesNotResolveUnknownOffset()
    {
        var fixture = new Fixture(false);
        var (method, instruction, _) = fixture.Load(fixture.Derived, 1000);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.False);
        Assert.That(instruction.Operands[1], Is.TypeOf<MemoryOperand>());
    }

    [Test]
    public void DoesNotGuessValueTypeGenericLayout()
    {
        var fixture = new Fixture(false);
        var value = fixture.Type("UnknownStruct", fixture.Type("ValueType", null, "System"));
        fixture.Derived.OverrideBaseType = fixture.Definition.MakeGenericInstanceType(value);
        var (method, _, _) = fixture.Load(fixture.Derived, 3 * fixture.PointerSize);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.False);
    }

    [Test]
    public void DoesNotGuessUnknownAncestorSize()
    {
        var fixture = new Fixture(false);
        fixture.NativeField.FieldType = fixture.Type("UnknownStruct", fixture.Type("ValueType", null, "System"));
        var (method, _, _) = fixture.Load(fixture.Derived, 3 * fixture.PointerSize);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.False);
    }

    [Test]
    public void DoesNotGuessAncestorTailPadding()
    {
        var fixture = new Fixture(false);
        fixture.NativeField.FieldType = fixture.Type("Byte", fixture.Type("ValueType", null, "System"), "System");
        var (method, _, _) = fixture.Load(fixture.Derived, 2 * fixture.PointerSize + 1);
        Assert.That(MetadataResolver.ResolveFieldOffsets(method), Is.False);
    }

    private sealed class Fixture
    {
        public readonly int PointerSize;
        private readonly InjectedAssemblyAnalysisContext assembly;
        public readonly InjectedTypeAnalysisContext Text, Definition, Derived;
        public readonly GenericParameterTypeAnalysisContext Parameter;
        public readonly GenericInstanceTypeAnalysisContext GenericBase;
        public readonly InjectedFieldAnalysisContext NativeField, Identifier;

        public Fixture(bool is32Bit)
        {
            // Only pointer size is needed; no proprietary metadata or native binary.
            var binary = (LibCpp2IL.PE.PE)RuntimeHelpers.GetUninitializedObject(typeof(LibCpp2IL.PE.PE));
            binary.is32Bit = is32Bit;
            var library = (LibCpp2IlContext)RuntimeHelpers.GetUninitializedObject(typeof(LibCpp2IlContext));
            typeof(LibCpp2IlContext).GetProperty(nameof(LibCpp2IlContext.Binary))!.SetValue(library, binary);
            typeof(LibCpp2IlContext).GetProperty(nameof(LibCpp2IlContext.Metadata))!.SetValue(library,
                RuntimeHelpers.GetUninitializedObject(typeof(LibCpp2IL.Metadata.Il2CppMetadata)));
            var app = (ApplicationAnalysisContext)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationAnalysisContext));
            app.LibCpp2IlContext = library;
            assembly = new InjectedAssemblyAnalysisContext("Synthetic", app);
            PointerSize = binary.PointerSizeBytes;
            Text = Type("String", null, "System");
            var nativeBase = Type("NativeBase", null);
            NativeField = nativeBase.InjectFieldContext("NativeReference", Text, FieldAttributes.Public);
            NativeField.Offset = 2 * PointerSize;
            Definition = Type("GenericBase", nativeBase);
            Parameter = new GenericParameterTypeAnalysisContext("T", 0, Il2CppTypeEnum.IL2CPP_TYPE_VAR, 0, Definition);
            Definition.GenericParameters.Add(Parameter);
            Definition.InjectFieldContext("IgnoredStatic", Text, FieldAttributes.Public | FieldAttributes.Static);
            Identifier = Definition.InjectFieldContext("Identifier", Text, FieldAttributes.Public);
            Identifier.Offset = 0; // Generic definitions have no concrete metadata offset.
            GenericBase = Definition.MakeGenericInstanceType(Text);
            Derived = Type("Derived", GenericBase);
        }

        public InjectedTypeAnalysisContext Type(string name, TypeAnalysisContext? parent, string ns = "Synthetic")
            => new(assembly, ns, name, parent, TypeAttributes.Public);

        public (MethodAnalysisContext, Instruction, LocalVariable) Load(TypeAnalysisContext type, long offset)
        {
            var receiver = new LocalVariable("this", new Register(null, "rcx")) { Type = type };
            var destination = new LocalVariable("result", new Register(null, "rax"));
            var instruction = new Instruction(0, OpCode.Move, destination, new MemoryOperand(receiver, addend: offset));
            var method = (MethodAnalysisContext)RuntimeHelpers.GetUninitializedObject(typeof(MethodAnalysisContext));
            method.ControlFlowGraph = new ISILControlFlowGraph([instruction, new Instruction(1, OpCode.Return, destination)]);
            return (method, instruction, receiver);
        }
    }
}
