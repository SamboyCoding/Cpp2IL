# Cpp2IL

WIP Tool to reverse Unity's IL2CPP build process back to the original managed DLLs.

**Does not currently generate IL code, only generates pseudocode and textual analysis. Work is being done to improve this. See the roadmap below**

Every commit is built to a release, so just check the github releases for the latest build.

Uses LibCpp2IL for the initial parsing and loading of metadata structures. LibCpp2IL is obtainable from the build artifacts if you want to do something yourself with IL2CPP metadata, and is released under the MIT license.

On top of that it then examines the machine code to attempt to perform static analysis and obtain some semblance of what the original code does.

## A note on unity 2020.2

Unity 2020.2 introduced IL2PP metadata version 27. Substantial changes have been made to the formats.

As of Feb 14th, 2021, LibCpp2IL now supports Metadata v27 and v27.1 (Unity 2020.2.4), and DummyDLLs can be generated from games using these versions.

However, due to some of the reshuffling of internal structures (namely, metadata usages) the analysis portion of Cpp2IL will not work for games targeting 2020.2 or later.

## A note on x86-32 support

While LibCpp2IL fully supports the loading of 32-bit assemblies, the static analysis Cpp2IL does on top of that is built around 64-bit assemblies, so does not work well in these cases due to the substantial differences between the formats used.

Once the current rewrite is finished, work will be done to improve this.

## General Roadmap

Subject to change

- [x] ~~Split code out into LibCpp2Il~~
- [ ] Rewrite Cpp2IL's analysis to use the new Action system
- [ ] Add IL Generation
- [ ] Look into adding support for remaining Actions to improve analysis.
- [ ] IL2CPP Metadata v27 Support
- [ ] x86-32 support
- [ ] Migrate to 0xd4d's iced decompiler?
- [ ] ARM support.


## Credit

Built using .NET Core 3.1, SharpDiasm, and Mono's Cecil.
