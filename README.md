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

Cpp2IL is currently undergoing a major rewrite. This branch represents work in progress, and is subject to change.

At present, the ability to dump IL for method bodies has been disabled. If you need this, you can try an [older release](https://github.com/SamboyCoding/Cpp2IL/releases/tag/2022.0.7) (with [older README](https://github.com/SamboyCoding/Cpp2IL/tree/new-analysis)). Support has always been experimental, you may be better off using [il2cppdumper](https://github.com/Perfare/Il2CppDumper?tab=readme-ov-file), then using a dissassembler it makes scripts for.

### Development Branch Notes

CI builds for developers can be obtained from [My Nuget Feed](https://nuget.samboy.dev/). 

The command-line interface has been simplified, going from a lot of command line options to a concept of output formats
and processing layers. However, a lot of these formats and layers are not yet implemented, so functionality is limited
compared to the previously released versions.

#### Obvious Changes:

Many options, such as `--analysis-level`, `--skip-analysis`, etc, have been removed. Ignoring the fact that analysis is not yet implemented, these options will not be coming back.
Analysis will be off by default, and will be enabled via the usage of a processing layer. 

Equally, options like `--supress-attributes`, which previously suppressed the Cpp2ILInjected attributes, have been replaced with a process layer - this one is actually implemented,
and is called `attributeinjector`. You can enable this layer using the `--use-processor` option, and you can list other options using `--list-processors`. 

Metadata dumps and method dumps will be their own output format too, instead of both being default-on, and controlled via a dedicated option. Currently this means you'll need to run
Cpp2IL multiple times if you want both dumps, though this may change in the future if we add support for outputting to multiple formats simultaneously. Like processing layers,
output formats can be listed via the `--list-output-formats` option, and are selected via the `--output-as` option.

#### Less obvious changes:

Under the hood, the application has been almost completely rewritten. Primarily, this was necessary due to the degree Cpp2IL was dependent on the Mono.Cecil library, which had some
limitations. When we looked into switching, we realised how reliant we were on the library. This is no longer the case - the application is written around LibCpp2IL types and 
the new Analysis Context objects, and the Mono.Cecil library is no longer used, having been replaced with AsmResolver.DotNet. 

On top of that, we are currently in the process of reimplementing analysis based around an intermediate representation called ISIL (Instruction-Set-Independent Language), which
will allow for much easier support of new instruction sets. The ISIL is then converted into a Control Flow Graph, which can be analysed more intelligently than a raw disassembly.

We're also working on a Plugin system which will allow third-party developers to write plugins to add support for custom instruction sets, binary formats, and eventually load 
obfuscated or encrypted metadata or binary files. 

## Command Line Options

### Basic Usage

The simplest usage of this application is for a windows x86 or x64 unity game. In that case you can just
run `Cpp2IL-Win.exe --game-path=C:\Path\To\Your\Game`
and Cpp2IL will detect your unity version, locate the files it needs, and dump the output into a cpp2il_out folder
wherever you ran the command from.

Assuming you have a single APK file (not an APKM or XAPK), and are running at least cpp2il 2021.4.0, you can use the
same argument as above but pass in the path to the APK, and cpp2il will extract the files it needs from the APK.

### Supported Command Line Option Listing

|        Option         |       Argument Example        |                                                                                         Description                                                                                          |
|:---------------------:|:-----------------------------:|:--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------:|
|      --game-path      |        C:\Path\To\Game        |                                                                        Specify the path to the game folder. Required.                                                                        |
|      --exe-name       |           TestGame            |                                Specify the name of the game's exe file in case auto detection fails (because there are other exe files in the game directory)                                |
|       --verbose       |           &lt;None>           |                                                                         Log more information about what we are doing                                                                         |
|   --list-processors   |           &lt;None>           |                                                                         List available processing layers, then exit.                                                                         |
|    --use-processor    |       attributeinjector       |                                 Select a processing layer to use, which can change the raw data prior to outputting. This option can appear multiple times.                                  |
|  --processor-config   |           key=value           |                           Provide configuration options to the selected processing layers. These will be documented by the plugin which adds the processing layer.                           |
| --list-output-formats |           &lt;None>           |                                                                          List available output formats, then exit.                                                                           |
|      --output-as      |           dummydll            |                                                                          Specify the output format you wish to use.                                                                          |
|      --output-to      |          cpp2il_out           |                     Root directory to output to. This path will be passed to the selected output format, which may then create subdirectories etc. within this location.                     |
| --wasm-framework-file | C:\Path\To\webgl.framework.js | Only used in conjunction with WASM binaries. Some of these have obfuscated exports but they can be recovered via a framework.js file, which you can provide the path to using this argument. |

## Release Structure

Every single commit is built to a CI build using Github Actions - the action file can be found in the .github folder,
if you want to reproduce the builds yourself. Be aware these may not be the most stable - while there are tests to
ensure compatibility with a range of games, sometimes things do break! These are versioned by the commit they were built
from.

The release files can be downloaded from the Actions tab if you are signed into GitHub, or you can use the following links,
which always point to the latest successful CI build. Note that the .NET Framework build is provided for compatibility with 
wine/proton.

- [Windows Native Build](https://nightly.link/SamboyCoding/Cpp2IL/workflows/dotnet-core/development/Cpp2IL-net7-win-x64.zip)
- [Linux Native Build](https://nightly.link/SamboyCoding/Cpp2IL/workflows/dotnet-core/development/Cpp2IL-net7-linux-x64.zip)
- [Mac Native Build](https://nightly.link/SamboyCoding/Cpp2IL/workflows/dotnet-core/development/Cpp2IL-net7-osx-x64.zip)
- [.NET Framework 4.7.2 Windows Build](https://nightly.link/SamboyCoding/Cpp2IL/workflows/dotnet-core/development/Cpp2IL-Netframework472-Windows.zip)


On top of this, I manually release "milestone" release builds whenever I think a major set of improvements have been made. These
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

## Credits

This application is built primarily using .NET 7.0, but a .NET Framework 4.7.2 build is also published for legacy purposes.

It uses the following libraries, for which I am very thankful:

(All are MIT licensed aside from XUnit which is Apache 2.0+MIT)

- [iced](https://github.com/icedland/iced) disassembler for x86
- [Capstone.NET](https://github.com/9ee1/Capstone.NET) for ARMv8 and ARMv7 disassembly.
- My own WasmDisassembler library for WebAssembly disassembly. This can be found in the `WasmDisassembler` subdirectory.
- [Pastel](https://github.com/silkfire/Pastel) for the console colours.
- [CommandLineParser](https://github.com/commandlineparser/commandline) so I didn't need to write one myself.
- [AsmResolver](https://github.com/Washi1337/AsmResolver) for any output formats which produce managed .NET assemblies.
- [xUnit](https://github.com/xunit/xunit) for the unit tests.
- [IndexRange](https://github.com/bgrainger/IndexRange) to port System.Index and System.Range back to netstandard2.0.
- [Nullable](https://github.com/manuelroemer/Nullable) to port nullable attributes back to netstandard2.0.

Finally, the OrbisPkg plugin uses [LibOrbisPkg](https://github.com/maxton/LibOrbisPkg), which is licensed under the LGPL, version 3.

Cpp2IL is (very loosely, at this point) based off of [Il2CppDumper](https://github.com/Perfare/Il2CppDumper), which I forked
in 2018 and removed a lot of code, rewrote a lot, and added a lot more. But at its core, it's still got some dumper left
in it, mostly in LibCpp2IL.

It contains bits and pieces from [Il2CppInspector](https://github.com/djkaty/Il2CppInspector/), taken with permission
from djKaty, and I'd like to express my gratitude to her here for her invaluable help.

I'd like to thank the Audica Modding community and Discord for the initial inspiration for this project, lots of support
in the early days, and feature requests these days.

And finally, check out some other cool projects which link in with this one. Of course, I
mentioned [Il2CppAssemblyUnhollower](https://github.com/knah/Il2CppAssemblyUnhollower/)
further up, but also check out [MelonLoader](https://github.com/LavaGang/MelonLoader/), which uses Cpp2IL for Dummy DLL
generation.
