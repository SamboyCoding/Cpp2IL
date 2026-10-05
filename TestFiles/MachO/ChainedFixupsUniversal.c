// Source of ChainedFixupsUniversal.dylib: a universal (x86_64 + arm64) Mach-O whose slices use
// chained fixups (macOS 12+ deployment target). __DATA,__data holds exactly two pointers, each
// pointing to itself, stored as one chain of two rebases. Built on macOS with:
// clang -arch x86_64 -arch arm64 -mmacosx-version-min=12.0 -Os -dynamiclib -Wl,-x \
//     -o ChainedFixupsUniversal.dylib ChainedFixupsUniversal.c && strip -x ChainedFixupsUniversal.dylib

void *fixture_first = &fixture_first;
void *fixture_second = &fixture_second;
