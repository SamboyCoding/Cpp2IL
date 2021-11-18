# LibCpp2IL

[![NuGet](https://img.shields.io/nuget/v/Samboy063.LibCpp2IL)](https://www.nuget.org/packages/Samboy063.LibCpp2IL/)

Provides an API for working with IL2CPP-generated metadata and game assemblies.

## Downloading

You can obtain a copy of LibCpp2IL from the [actions page](https://github.com/SamboyCoding/Cpp2IL/actions) - click the most recent successful build, and click "LibCpp2IL" to download a zipped copy of the dll, pdb, and dependency JSON.

## Setting up

Setting up the library can be done in one of two ways. At present, debug logging is turned on, so the library will call Console.WriteLine with a large amount of log data showing what it is currently loading, as well as timing data.

### From the hard drive:
```c# 
var unityVersion = new [] {2019, 2, 0}; //You'll have to get this from globalgamemanagers or the unity engine exe's file version.
if (!LibCpp2IlMain.LoadFromFile(gameAssemblyPath, globalMetadataPath, unityVersion)) {
    Console.WriteLine("initialization failed!");
    return;
}
```
### From a byte array
You can also load the two files manually and provide their content to the library:
```c# 
var unityVersion = new [] {2019, 2, 0}; //You'll have to get this from globalgamemanagers or the unity engine exe's file version.
if (!LibCpp2IlMain.Initialize(gameAssemblyBytes, globalMetadataBytes, unityVersion)) {
    Console.WriteLine("initialization failed!");
    return;
}
```

Initializing with either of these methods will populate the `LibCpp2IlMain.ThePe` and `LibCpp2IlMain.TheMetadata` fields, if you want low-level access.

## Global constants

IL2CPP stores type, field, string literal, and method references as globals. Given the virtual address of one of these globals - for example a type reference is passed into every call to il2cpp_codegen_object_new - you can use LibCpp2IL to get the associated object.

### Type References

```c#
//Il2CppTypeReflectionData is a wrapper around type definitions to allow for generic params and arrays.
Il2CppTypeReflectionData type = LibCpp2IlMain.GetTypeGlobalByAddress(0x180623548);
```

### Method References

Method references can be generic or otherwise, but it's not possible currently to get the pointer to a generic variant of a method - though it is possible to work out what params a given global refers to.

If you just want method details, use this:
```c#
Il2CppMethodDefinition method = LibCpp2IlMain.GetMethodDefinitionByGlobalAddress(0x182938239);
```

If you want more complex data, such as type and/or method generic params, you can use this code to get the raw global struct, and obtain the generic data like so:
```c#
MetadataUsage? usage = LibCpp2IlMain.GetMethodGlobalByAddress(0x182938239);
if(usage == null) return;

if(usage.Type == MetadataUsageType.MethodRef) {
    var genericMethodRef = usage.AsGenericMethodRef();
    Console.WriteLine(genericMethodRef.declaringType); //Il2CppTypeDefinition
    Console.WriteLine(genericMethodRef.baseMethod); //Il2CppMethodDefinition, equal to the one returned by the above method
    Console.WriteLine(genericMethodRef.typeGenericParams); //Il2CppTypeReflectionData[]
    Console.WriteLine(genericMethodRef.methodGenericParams); //Il2CppTypeReflectionData[]
} else {
    //Method is not generic
    Console.WriteLine(usage.AsMethod()); //Il2CppMethodDefinition
}
```

### Field References

```c#
Il2CppFieldDefinition fieldDef = LibCpp2IlMain.GetFieldGlobalByAddress(0x182933215);
```

### String Literals

```c#
string literal = LibCpp2IlMain.GetLiteralByAddress(0x182197654);
```

## Reflection API

LibCpp2IL provides utility methods to get types by name in order to start the Reflection process.

```c#
//Signature:
Il2CppTypeDefinition type = typeLibCpp2IlReflection.GetType(typeName, optionalNamespaceName);

//Examples:
Il2CppTypeDefinition type = LibCpp2IlReflection.GetType("String");
type = LibCpp2IlReflection.GetType("List`1");
type = LibCpp2IlReflection.GetType("Object", "UnityEngine");
```

## Accessing type properties

Convenience methods are provided to obtain the most commonly used properties of a type, such as its methods, fields, properties, and events, in addition to its name, namespace, and hierarchy data such as declaring type, base class, interfaces etc.

Given the definition of `type` as such:
```c#
Il2CppTypeDefinition type = LibCpp2IlReflection.GetType("String");
```

### Tokens

As a quick note, all fields, types, methods, properties, and events store their token in the `token` field.

### Basic Properties
```c#
Console.WriteLine(type.Namespace); //System
Console.WriteLine(type.Name); //String
Console.WriteLine(type.FullName); //System.String
```

### Inheritance
```c#
//Base class
Console.WriteLine(type.BaseType.FullName); //System.Object

//Interface data, including generic parameters.

//Il2CppTypeReflectionData is a wrapper around Il2CppTypeDefinition that allows for generics.
//ToString on these returns their canonical form.
Il2CppTypeReflectionData[] interfaces = type.Interfaces;
var enumerableOfChar = interfaces[5];
Console.WriteLine(enumerableOfChar); //System.Collections.Generic.IEnumerable`1<System.Char>
Console.WriteLine(enumerableOfChar.isType); //true
Console.WriteLine(enumerableOfChar.isGenericType); //true
Console.WriteLine(enumerableOfChar.baseType.FullName); //System.Collections.Generic.IEnumerable`1
Console.WriteLine(enumerableOfChar.genericParams[0]); //System.Char
Console.WriteLine(enumerableOfChar.genericParams[0].isType); //true
Console.WriteLine(enumerableOfChar.genericParams[0].isGenericType); //false
```

### Methods
```c#
//In this example, string.Join(string separator, string[] value) is the first-defined method in the metadata, but it could be a different order.
var join = type.Methods[0];

Console.Log(join.Name); //Join
Console.Log($"0x{join.MethodPointer:X}"); //0x180385033
//Getting the file address of a method
Console.Log($"Join is in-assembly at address 0x{LibCpp2IlMain.ThePe.MapVirtualAddressToRaw(join.MethodPointer):X}"); //Join is in-assembly at address 0x385033
//ReturnType is a ReflectionData again, like interfaces are
Console.Log(join.ReturnType); //System.String
//DeclaringType gives you the original Il2CppTypeDefinition back
Console.Log(join.DeclaringType.FullName); //System.String
//Parameters are Il2CppParameterReflectionData objects, objects to contain information on a parameter, such as its name, type, and default value.
//ToString on these also returns their canonical form.
Console.Log(join.Parameters[0].Type) //System.String
Console.Log(join.Parameters[1].Type) //System.String[]
Console.Log(join.Parameters[1].Type.isType) //false
Console.Log(join.Parameters[1].Type.isArray) //true
Console.Log(join.Parameters[1].Type.arrayType) //System.String
```

### Fields
```c#
//Accessing the string's internal length field. Note that unlike methods, fields DO have a defined order and they are presented in that order.
var lengthField = type.Fields[0];
Console.WriteLine(lengthField.Name); //m_stringLength
//FieldType is an Il2CppTypeReflectionData, the ToString of which gives the name of the class.
Console.WriteLine(lengthField.FieldType); //System.Int32
```

### Properties

```c#
var lengthProperty = type.Properties[1];
Console.WriteLine(lengthProperty.Name); //Length
Console.WriteLine(lengthProperty.Getter.Name); //get_Length
Console.WriteLine(lengthProperty.Setter); //null
//PropertyType is an Il2CppTypeReflectionData object
Console.WriteLine(lengthProperty.PropertyType); //System.Int32
Console.WriteLine(lengthProperty.DeclaringType.FullName); //System.String
```

### Nested Types
```c#
var transform = LibCpp2IlReflection.GetType("Transform", "UnityEngine");
Console.Log(transform.NestedTypes[0].Name); //Enumerator
Console.Log(transform.NestedTypes[0].DeclaringType.FullName); //UnityEngine.Transform
```

### Events
```c#
var appDomain = LibCpp2IlReflection.GetType("AppDomain", "System");
Console.Log(appDomain.Events.Length); //3
Console.Log(appDomain.Events[0].Name); //DomainUnload
Console.Log(appDomain.Events[0].EventType); //System.EventHandler
//Adder returns an Il2CppMethodDefinition and represents the method used to add a listener to the event.
//Remover also exists, as does Invoker.
Console.Log(appDomain.Events[0].Adder.Name); //add_DomainUnload

//Type returns an Il2CppTypeReflectionData, as it's just a standard method parameter.
Console.Log(appDomain.Events[0].Adder.Parameters[0].Type); //System.EventHandler
```

### Generic Methods

Due to the nature of structures in C++, generic methods have to have variants based on the size (in bytes) of their generic parameters.

Consider a 64-bit assembly. The size of an object (which would be 8 bytes, as it's a pointer) and an `int`, or rather, a `System.Int32` (which would be 4 bytes) are different.

So `List<T>` needs to have different implementations of its methods depending on if it's a `List<Some Object>` versus a `List<int>`.

And IL2CPP strips out any implementations that the game itself doesn't - and won't ever - use.

So, given an `Il2CppMethodDefinition` for a generic method, you can find out which implementations DO exist by accessing the ConcreteGenericMethods dict on a PE object, like so.

```c#
var listType = LibCpp2IlReflection.GetType("List`1", "System");
var addMethod = listType.Methods.First(m => m.Name == "Add");
var variants = LibCpp2IlMain.ThePe.ConcreteGenericMethods[addMethod];
Console.WriteLine(addMethod.MethodPointer); //0xdeadbeef
Console.WriteLine(variants[0].BaseMethod.MethodPointer); //Same as above
Console.WriteLine(variants[0].GenericParams[0]); //Could be, for example, "System.Int32"
//The below may or may not give the original method pointer.
//The original pointer is the implementation for `T`.
//This will be the same as the implementation for `object`
//This variant, assuming it is for Int32, should be a different pointer.
//If it's for a class such as `string`, it will be the same pointer.
Console.WriteLine(variants[0].GenericVariantPtr); //0x123456789
```   

Alternatively, if you have a call to an address 0x123456789 and you know there's no defined method at that address, you can check for generic implementations and obtain the base definition by using the following code:
```c#
var genericImplementations = LibCpp2IlMain.ThePe.ConcreteGenericImplementationsByAddress[0x123456789];
Console.WriteLine(genericImplementations[0].BaseMethod.HumanReadableSignature);
```  