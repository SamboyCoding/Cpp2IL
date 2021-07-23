# Cpp2IL

[![NuGet](https://img.shields.io/nuget/v/Samboy063.Cpp2IL.Core)](https://www.nuget.org/packages/Samboy063.Cpp2IL.Core/)

WIP Tool to reverse Unity's IL2CPP build process back to the original managed DLLs.

The information below almost entirely applies to the CLI application available on github releases. For documentation on 
using the "core" module - which the CLI is just a wrapper around - in your own projects, see [README_CORE.md](Cpp2IL.Core/README_CORE.md)

**IL Generation is currently to-file only, not to-assembly. Don't expect to magically get managed DLLs out of this.**

Uses [LibCpp2IL](LibCpp2IL) for the initial parsing and loading of metadata structures. 
LibCpp2IL is obtainable from the build artifacts if you want to do something yourself with IL2CPP metadata, 
and is released under the MIT license.
The link above will take you to the documentation for LibCpp2IL.

## Release Structure

Every single commit is built to a pre-release using Github Actions - the action file can be found in the .github folder,
if you want to reproduce the builds yourself. Be aware these may not be the most stable - while there are tests to ensure
compatibility with a range of games, sometimes things do break! These are versioned by the commit they were built from.

On top of this, I manually release "milestone" builds whenever I think a major set of improvements have been made. These
are NOT marked as pre-releases on github, and should (at least in theory) be stable and suitable for use on a range of games.

## Terminal Colors and Debug Logging

From the first milestone build 2021.0, and onwards, Cpp2IL now outputs more rigidly-structured data to the console. 
This includes log levels (VERB, INFO, WARN, FAIL) and associated colours (Grey for VERB, Blue for INFO, Yellow for 
WARN, Red for FAIL).

As of milestone 2021.1, if Cpp2IL is able to detect that you're running in Wine/Proton, these ANSI colour codes are disabled,
as they are not supported by wine and look awful.

VERB messages will only be logged if Cpp2IL is launched with the `--verbose` option, and it would be helpful if you 
could report issues with this flag enabled. For normal operation, they shouldn't be needed, unless you're curious.

If you do not wish for the output to be coloured, set the Environment Variable `NO_COLOR=true`.

## What Works (Features)

- [x] Loading of Metadata and Binaries using LibCpp2IL for IL2CPP versions 24 through 27.1 (unity 2018 to present-day) 
- [x] "Dummy DLL" (Stub Assembly) generation, suitable for use with [Il2CppAssemblyUnhollower](https://github.com/knah/Il2CppAssemblyUnhollower/), for PE and ELF binaries, x86 and ARM instruction sets
- [x] Restoration of explicit override methods in managed types. This data is not explicitly saved to the Il2Cpp metadata, but is useful for Unhollower.
- [x] Il2CPP Api Function Detection
- [x] Analysis of both x86_32 and x86_64 machine code for the following features:
    - [x] Managed Function Calls (including virtual calls via vtable)
    - [x] Managed Function Argument Resolution
    - [x] Object Instantiation
    - [x] String Constant Loads
    - [x] Instance field assignments
    - [x] Instance field reads
    - [x] Static field assignments
    - [x] Static field reads
    - [x] IL2CPP "Exception Helper" function detection (shown as `throw new ExceptionType()` in pseudocode)
    - [x] Array instantiation
    - [x] Array assignment by index
    - [x] Array read by index
    - [x] Array length reads
    - [x] If/White/Else If/Else detection
    - [x] Mathematical subtraction
    - [x] Mathematical addition
    - [x] Some mathematical multiplication
    - [x] Return statements, including returned variables
    - [x] FPU (Floating-Point processor) support for x86_32 games.
    - [x] RGCTX (Runtime Generic Context) Support
- [x] IL Generation to a text file (not saved to the DLL yet)
- [x] Significantly faster than both Il2CppDumper and Il2CppInspector (for DummyDLL Generation)
    

## What's work in progress (Roadmap)

(Subject to change)

- [ ] Save generated IL to the actual function body in the Assembly.
- [ ] Look into adding support for remaining Actions to improve analysis. Some key areas I'm focussing on:
    - [ ] Integer division via magic constant multiplication.
    - [ ] Wider support for multiplication (IMUL instructions)
- [ ] Machine code analysis for ARM instructions (long-term goal, want to do a lot more for x86 instruction set first)


## Credits

This application is built using .NET 5.0.

It uses the following libraries, for which I am very thankful:

- [iced](https://github.com/icedland/iced) disassembler for x86
- [Pastel](https://github.com/silkfire/Pastel) for the console colours.
- [CommandLineParser](https://github.com/commandlineparser/commandline) so I didn't need to write one myself.
- [Mono.Cecil](https://github.com/jbevain/cecil/) to create and save the Dummy DLLs, and generate IL.
- [xUnit](https://github.com/xunit/xunit) for the unit tests.

It's (very loosely, at this point) based off of [Il2CppDumper](https://github.com/Perfare/Il2CppDumper), which I 
forked in 2018 and removed a lot of code, rewrote a lot, and added a lot more. But at its core, it's still got some dumper
left in it.

It contains bits and pieces from [Il2CppInspector](https://github.com/djkaty/Il2CppInspector/), taken with permission
from djKaty, and I'd like to express my gratitude to her here for her invaluable help.

I'd like to thank the Audica Modding community and Discord for the initial inspiration for this project, lots of
support in the early days, and feature requests these days.

And finally, check out some other cool projects which link in with this one. Of course, I mentioned [Il2CppAssemblyUnhollower](https://github.com/knah/Il2CppAssemblyUnhollower/)
further up, but also check out [MelonLoader](https://github.com/LavaGang/MelonLoader/), which uses Cpp2IL for Dummy DLL generation.
