# Cpp2IL

WIP Tool to reverse Unity's IL2CPP build process back to the original managed DLLs.

**Does not currently generate IL code, only generates pseudocode and textual analysis. Work is being done to improve this.**

**Has issues with 32-bit games due to substantial differences between them and 64-bit games. Again, work is being done on this.**

Every commit is built to a release, so just check the github releases for the latest build.

Built using .NET Core 3.1, SharpDiasm, and Mono's Cecil.
