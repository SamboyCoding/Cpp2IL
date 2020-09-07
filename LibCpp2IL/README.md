# LibCpp2IL

Provides an API for working with IL2CPP-generated metadata and game assemblies.

## Setting up

Setting up the library can be done in one of two ways. At present, debug logging is turned on, so the library will call Console.WriteLine with a large amount of log data showing what it is currently loading, as well as timing data.

### From the hard drive:
```cs 
var unityVersion = new [] {2019, 2, 0}; //You'll have to get this from globalgamemanagers or the unity engine exe's file version.
if (!LibCpp2IlMain.LoadFromFile(gameAssemblyPath, globalMetadataPath, unityVersion)) {
    Console.WriteLine("initialization failed!");
    return;
}
```
### From a byte array
You can also load the two files manually and provide their content to the library:
```cs 
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

```cs
//Il2CppTypeReflectionData is a wrapper around type definitions to allow for generic params and arrays.
Il2CppTypeReflectionData type = LibCpp2IlMain.GetTypeGlobalByAddress(0x180623548);
```

### Method References

Method references can be generic or otherwise, but it's not possible currently to get the pointer to a generic variant of a method - though it is possible to work out what params a given global refers to.

If you just want method details, use this:
```cs
Il2CppMethodDefinition method = LibCpp2IlMain.GetMethodDefinitionByGlobalAddress(0x182938239);
```

If you want more complex data, such as type and/or method generic params, you can use this code to get the raw global struct, and obtain the generic data like so:
```cs
GlobalIdentifier? nullableGlobal = LibCpp2IlMain.GetMethodGlobalByAddress(0x182938239);
if(!nullableGlobal.HasValue) return;

var global = nullableGlobal.Value;
if(global.Value is Il2CppGlobalGenericMethodRef genericMethodRef) {
    Console.WriteLine(genericMethodRef.declaringType); //Il2CppTypeDefinition
    Console.WriteLine(genericMethodRef.baseMethod); //Il2CppMethodDefinition, equal to the one returned by the above method
    Console.WriteLine(genericMethodRef.typeGenericParams); //Il2CppTypeReflectionData[]
    Console.WriteLine(genericMethodRef.methodGenericParams); //Il2CppTypeReflectionData[]
} else if(global.Value is Il2CppMethodDefinition methodDefinition) {
    //Method is not generic
    Console.WriteLine(methodDefinition);
}
```

### Field References

```cs
Il2CppFieldDefinition fieldDef = LibCpp2IlMain.GetFieldGlobalByAddress(0x182933215);
```

### String Literals

```cs
string literal = LibCpp2IlMain.GetLiteralByAddress(0x182197654);
```

## Reflection API

LibCpp2IL provides utility methods to get types by name in order to start the Reflection process.

```cs
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
```cs
Il2CppTypeDefinition type = LibCpp2IlReflection.GetType("String");
```

### Tokens

As a quick note, all fields, types, methods, properties, and events store their token in the `token` field.

### Basic Properties
```cs
Console.WriteLine(type.Namespace); //System
Console.WriteLine(type.Name); //String
Console.WriteLine(type.FullName); //System.String
```

### Inheritence
```cs
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
```cs
//In this example, string.Join(string separator, string[] value) is the first-defined method in the metadata, but it could be a different order.
var join = type.Methods[0];

Console.Log(join.Name); //Join
Console.Log($"0x{join.MethodPointer:X}"); //0x180385033
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
```cs
//Accessing the string's internal length field. Note that unlike methods, fields DO have a defined order and they are presented in that order.
var lengthField = type.Fields[0];
Console.WriteLine(lengthField.Name); //m_stringLength
Console.WriteLine(lengthField.FieldType.FullName); //System.Int32
```

### Properties

```cs
var lengthProperty = type.Properties[1];
Console.WriteLine(lengthProperty.Name); //Length
Console.WriteLine(lengthProperty.Getter.Name); //get_Length
Console.WriteLine(lengthProperty.Setter); //null
//PropertyType is an Il2CppTypeReflectionData object
Console.WriteLine(lengthProperty.PropertyType); //System.Int32
Console.WriteLine(lengthProperty.DeclaringType.FullName); //System.String
```

### Nested Types
```cs
var transform = LibCpp2IlReflection.GetType("Transform", "UnityEngine");
Console.Log(transform.NestedTypes[0].Name); //Enumerator
Console.Log(transform.NestedTypes[0].DeclaringType.FullName); //UnityEngine.Transform
```

### Events
```cs
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
