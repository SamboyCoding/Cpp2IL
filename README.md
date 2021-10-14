# Cpp2IL

[![NuGet](https://img.shields.io/nuget/v/Samboy063.Cpp2IL.Core)](https://www.nuget.org/packages/Samboy063.Cpp2IL.Core/)

### Need Help? Join [the discord](https://discord.gg/XdggT7XZXm)!

WIP Tool to reverse Unity's IL2CPP build process back to the original managed DLLs.

The information below almost entirely applies to the CLI application available on github releases. For documentation on
using the "core" module - which the CLI is just a wrapper around - in your own projects,
see [README_CORE.md](Cpp2IL.Core/README_CORE.md)

Uses [LibCpp2IL](LibCpp2IL) for the initial parsing and loading of metadata structures. LibCpp2IL is obtainable from the
build artifacts if you want to do something yourself with IL2CPP metadata, and is released under the MIT license. The
link above will take you to the documentation for LibCpp2IL.

## Command Line Options

### Basic Usage

The simplest usage of this application is for a windows x86 or x64 unity game. In that case you can just
run `Cpp2IL-Win.exe --game-path=C:\Path\To\Your\Game`
and Cpp2IL will detect your unity version, locate the files it needs, and dump the output into a cpp2il_out folder
wherever you ran the command from.

Assuming you have a single APK file (not an APKM or XAPK), and are running at least cpp2il 2021.4.0, you can use the
same argument as above but pass in the path to the APK, and cpp2il will extract the files it needs from the APK.

### Supported Command Line Option Listing

| Option | Argument Example | Description |
| :-----: | :--------------: | :----------: |
| --game-path | C:\Path\To\Game | Specify the path to the game folder. Required. |
| --exe-name | TestGame | Specify the name of the game's exe file in case auto detection fails (because there are other exe files in the game directory) |
| --analysis-level | 0 = Everything<br>1 = Skip Instruction Dump<br>2 = Skip Instruction Dump and Synopsis<br>3 = Print Only Generated IL<br>4 = Print Only Pseudocode | Specify what is saved to the `cpp2il_out/types/[Assembly]/typename_methods.txt` file. |
| --skip-analysis | &lt;None> | Flag to skip analysis entirely, only generating Dummy DLLs and optionally metadata dumps |
| --skip-metadata-txts | &lt;None> | Flag to skip metadata dumps (`cpp2il_out/types/[Assembly]/typename_metadata.txt`) | 
| --disable-registration-prompts | &lt;None> | Flag to prevent asking for the user to input addresses in STDIN if they can't be detected |
| --verbose | &lt;None> | Log more information about what we are doing |
| --experimental-enable-il-to-assembly-please | &lt;None> | Attempt to save generated IL to the DLL file where possible. MAY BREAK THINGS. |
| --suppress-attributes | &lt;None> | Prevents generated DLLs from containing attributes providing il2cpp-specific metadata, such as function pointers, etc. |
| --parallel | &lt;None> | Run analysis in parallel. Usually much faster, but may be unstable. Also puts your CPU under a lot of strain (100% usage is targeted). |
| --run-analysis-for-assembly | mscorlib | Run analysis for the specified assembly. Do not specify the `.dll` extension. |
| --output-root | cpp2il_out | Root directory to output to. Dummy DLLs will be put directly in here, and analysis results will be put in a types folder inside. |
| --throw-safety-out-the-window | &lt;None> | When paired with `--experimental-enable-il-to-assembly-please`, do not abort attempting to generate IL for a method if an error occurs. Instead, continue on with the next action, skipping only the one which errored. WILL PROBABLY BREAK THINGS. |
| --analyze-all (only available in pre-release builds) | &lt;None> | Analyze all assemblies in the application |

## Release Structure

Every single commit is built to a pre-release using Github Actions - the action file can be found in the .github folder,
if you want to reproduce the builds yourself. Be aware these may not be the most stable - while there are tests to
ensure compatibility with a range of games, sometimes things do break! These are versioned by the commit they were built
from.

On top of this, I manually release "milestone" builds whenever I think a major set of improvements have been made. These
are NOT marked as pre-releases on github, and should (at least in theory) be stable and suitable for use on a range of
games.

## Terminal Colors and Debug Logging

From the first milestone build 2021.0, and onwards, Cpp2IL now outputs more rigidly-structured data to the console. This
includes log levels (VERB, INFO, WARN, FAIL) and associated colours (Grey for VERB, Blue for INFO, Yellow for WARN, Red
for FAIL).

As of milestone 2021.1, if Cpp2IL is able to detect that you're running in Wine/Proton, these ANSI colour codes are
disabled, as they are not supported by wine and look awful.

VERB messages will only be logged if Cpp2IL is launched with the `--verbose` option, and it would be helpful if you
could report issues with this flag enabled. For normal operation, they shouldn't be needed, unless you're curious.

If you do not wish for the output to be coloured, set the Environment Variable `NO_COLOR=true`.

## What Works (Features)

- [x] Loading of Metadata and Binaries using LibCpp2IL for IL2CPP versions 24 through 27.1 (unity 2018 to present-day)
- [x] "Dummy DLL" (Stub Assembly) generation, suitable for use
  with [Il2CppAssemblyUnhollower](https://github.com/knah/Il2CppAssemblyUnhollower/), for PE and ELF binaries, x86 and
  ARM instruction sets
- [x] Restoration of explicit override methods in managed types. This data is not explicitly saved to the Il2Cpp
  metadata, but is useful for Unhollower.
- [x] Il2CPP Api Function Detection
- [x] Flagship analysis of both x86_32 and x86_64 instruction sets.
- [x] Analysis for ARMv8/ARM64 machine code for a more limited set of operations than x86 (see the table below)
- [x] A framework for ARMv7 support, albeit with no operations supported yet.
- [x] Able to save generated IL to the actual function body in the Assembly, allowing decompilation using dnSpy/ILSpy.
- [x] Significantly faster than both Il2CppDumper and Il2CppInspector (for DummyDLL Generation)

## Supported Analysis Features Table

| Feature | Supported in x86 | Supported in ARMv8 | Supported in ARMv7 |
| :-----: | :--------------: | :----------------: | :----------------: |
| Simple Method Calls[^1] | ✔️ | ✔️ | ❌ |
| Virtual function calls (via vftable) | ✔️ | ❌ | ❌ |
| Interface function calls (via interfaceOffsets) | ✔️ | ❌ | ❌ |
| Argument resolution for function calls | ✔️ | ✔️ | ❌ |
| Object Instantiation | ✔️ | ✔️ | ❌ |
| Unmanaged String Literal Detection | ✔️ | ✔️ | ❌ |
| Instance field reads | ✔️ | ✔️ | ❌ |
| Instance field writes | ✔️ | ✔️ | ❌ |
| Static field reads | ✔️ | ❌ | ❌ |
| Static field writes | ✔️ | ❌ | ❌ |
| IL2CPP "Exception Helper" functions[^2] | ✔️ | ✔️ | ❌ |
| IL2CPP MetadataUsage parsing[^3] | ✔️ | ✔️ | ❌ |
| Array instantiation | ✔️ | ❌ | ❌ |
| Array offset reads | ✔️ | ❌ | ❌ |
| Array offset writes | ✔️ | ❌ | ❌ |
| Array length read | ✔️ | ❌ | ❌ |
| If/While/for/else if detection | ✔️ | Partial[^4] | ❌ |
| Mathematical operations | Partial[^5] | ❌ | ❌ |
| Floating point coprocessor support | ✔️ | N/A | N/A |
| RGCTX[^6] Support | ✔️ | ❌ | ❌ |
| Return statements, including return value detection | ✔️ | ✔️ | ❌ |

[^1]: A simple function call is one that is non-virtual, and not defined in an interface. This includes both static and instance functions.
[^2]: An exception helper is a function call which throws an exception, halting the execution of the current function. These are used for checks which are implicit in the .NET runtime, such as throwing NullReferenceExceptions if something is null and a field is accessed on it.
[^3]: A MetadataUsage is a reference to a type, field, method, generic instance method, or managed string literal.
[^4]: Analysis of ARMv8 binaries supports the following conditions in conditional statements: greater than, greater than or equal to, less than or equal to, not equal to null, not equal to, equal to null, equal to.
[^5]: x86 has a lot of opcodes for mathematical operations. Some are supported: Addition, subtraction, some multiplication (but not all), integer division.
[^6]: RGCTX stands for Runtime Generic ConTeXt, and is used to provide information about generic methods during runtime.

## What's work in progress (Roadmap)

(Subject to change)

- [ ] Ongoing: Wider support for actions to improve analysis accuracy. Some key points:
    - [ ] Wider support for x86 multiplication (IMUL instructions) as well as mathematical operations in general.
    - [ ] Possibly more x86 floating-point-related instructions.
    - [ ] Feature parity for Arm64 with X86. Most importantly: static fields, full range of conditions, managed array
      support, virtual functions, mathematical operations
- [ ] ARMv7 analysis. A template is present, but nothing specific runs.

## Credits

This application is built using .NET 5.0.

It uses the following libraries, for which I am very thankful:

(All are MIT licensed aside from XUnit which is Apache 2.0+MIT)

- [iced](https://github.com/icedland/iced) disassembler for x86
- [Capstone.NET](https://github.com/9ee1/Capstone.NET) for ARMv8 and ARMv7 disassembly.
- [Pastel](https://github.com/silkfire/Pastel) for the console colours.
- [CommandLineParser](https://github.com/commandlineparser/commandline) so I didn't need to write one myself.
- [Mono.Cecil](https://github.com/jbevain/cecil/) to create and save the Dummy DLLs, and generate IL.
- [HarmonyX](https://github.com/BepInEx/HarmonyX) to fix some of cecil's annoyingly vague error messages.
- [xUnit](https://github.com/xunit/xunit) for the unit tests.

It's (very loosely, at this point) based off of [Il2CppDumper](https://github.com/Perfare/Il2CppDumper), which I forked
in 2018 and removed a lot of code, rewrote a lot, and added a lot more. But at its core, it's still got some dumper left
in it.

It contains bits and pieces from [Il2CppInspector](https://github.com/djkaty/Il2CppInspector/), taken with permission
from djKaty, and I'd like to express my gratitude to her here for her invaluable help.

I'd like to thank the Audica Modding community and Discord for the initial inspiration for this project, lots of support
in the early days, and feature requests these days.

And finally, check out some other cool projects which link in with this one. Of course, I
mentioned [Il2CppAssemblyUnhollower](https://github.com/knah/Il2CppAssemblyUnhollower/)
further up, but also check out [MelonLoader](https://github.com/LavaGang/MelonLoader/), which uses Cpp2IL for Dummy DLL
generation.
