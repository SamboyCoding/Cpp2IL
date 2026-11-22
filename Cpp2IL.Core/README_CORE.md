# Cpp2IL.Core
## The magic behind the madness

This project contains all of the logic which makes Cpp2IL work. It's exposed as a set of semi-user-friendly APIs which you can call.

This documentation is work-in-progress.

### Initializing the Library

Cpp2IL.Core in turn depends on LibCpp2IL for interactions with the underlying metadata and binary files. For this reason it must be 
initialized before most of the other APIs can be invoked to give you any useful information.

This can be achieved via the `Cpp2IlApi.InitializeLibCpp2Il` function, which takes a number of parameters.
There are two overloads for this method - one which takes paths to files on disk, and one which takes a byte array of the content
of these files (in case you need to pre-process them to decrypt them, for example).

Beyond those two arguments, pass the Unity version as a `UnityVersion`, for example `new UnityVersion(2020, 3, 1)`. LibCpp2IL
accepts metadata versions 23 through 108 and uses the Unity version to select the exact layout for versions with multiple variants.

If your use case supports it, there is a utility method, `Cpp2IlApi.DetermineUnityVersion`, which takes two paths on disk - one to the Game's main executable, which is
only used if running on windows, and from which the file version is read and parsed, and one to the `GameName_Data` folder, which is used
as a backup on non-windows platforms and from which the unity version will be read from the `globalgamemanagers` file.

The only optional argument is `allowUserToInputAddresses`, which defaults to `false`. Set it to `true` only in interactive tools so
LibCpp2IL can ask for the CodeRegistration and MetadataRegistration addresses if it cannot locate them automatically.

### Generating "Dummy" Assemblies

This function requires the library to be initialized.

There is a single function, `Cpp2IlApi.MakeDummyDLLs`, which takes no arguments and generates + returns a List of Cecil 
`AssemblyDefinition`s which you can (but are not required to) use for your own purposes.

These assemblies contain the full type model of the il2cpp application, with all Types, Fields, Properties, Events, and Methods present.

However, the generated methods do NOT have a body (besides a stub one).
