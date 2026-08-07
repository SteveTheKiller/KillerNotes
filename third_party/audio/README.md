# Audio codec natives

Drop the **x64** DLLs in this folder and they get embedded into the exe automatically
(`KillerNotes.csproj`, the `AudioNative` `EmbeddedResource` block). Leave the folder empty and the
app still builds and runs - recordings just stay WAV. That is what the `Condition="Exists(...)"` on
each entry is for.

| File | Licence | Used for | Without it |
|---|---|---|---|
| `libFLAC.dll` | BSD-3-Clause | Recording **storage** | Recordings stored as WAV (~2x larger) |
| `libmp3lame.dll` | LGPL-2.1 | **Export only** (Share > Save as a file) | MP3 absent from the save dialog |

Both are x64 to match the SQLCipher native already embedded. `AudioNativeBootstrap` extracts them to
`%LOCALAPPDATA%\KillerNotes\native\<version>\` and preloads them; Costura only handles managed
assemblies, so a native cannot simply be referenced.

## Why FLAC stores and MP3 only exports

FLAC is lossless, so slicing a recording and saving it again is bit-identical every time. MP3 is
lossy - storing it would mean every edit costs a decode and a re-encode, and the generation loss
compounds. On a one-way copy out of the app that does not matter, and MP3 plays everywhere, which is
the entire point when sending a recording to someone.

## LGPL obligation (libmp3lame only)

LGPL-2.1 permits relicensing to GPLv2-or-later, so LAME sits inside this repo's GPLv3 without
trouble. But **distributing the binary obliges us to offer the corresponding source**, so keep the
exact source tarball the DLL was built from next to it in this folder, named to match the version.
libFLAC is BSD-3-Clause and carries no such obligation - attribution only.

## Provenance

There is no official first-party Windows binary for either project, unlike Tesseract's models, so
**these were cross-compiled from the upstream release tarballs** rather than taken from a mirror.
Nobody else's build is being trusted here.

### Source tarballs

| Tarball | From | SHA-256 |
|---|---|---|
| `flac-1.4.3.tar.xz` | `downloads.xiph.org/releases/flac/` | `6c58e69cd22348f441b861092b825e591d0b822e106de6eb0ee4d05d27205b70` |
| `lame-3.100.tar.gz` | `downloads.sourceforge.net/project/lame/lame/3.100/` | `ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e` |

Both match the hashes upstream publishes for those releases.

### Built binaries

| DLL | Version | SHA-256 |
|---|---|---|
| `libFLAC.dll` | 1.4.3 | `a9374801554762baf7e1d923e83b86502274bf16119daef09ecb21f6df75b3eb` |
| `libmp3lame.dll` | 3.100 | `0dd9ff2134da0664efa7434fe8ec20ff85ad36daf7296bd7aabcc1e3abefbaa3` |

Both PE32+ x64, stripped. Verify with `sha256sum` before a release; if either changes without a
deliberate rebuild, something is wrong.

### Reproducing the build

Toolchain: `llvm-mingw` 20250114 (ucrt, x86_64), unpacked from its GitHub release - no root needed,
which is why it was used over distro `mingw-w64`.

```sh
export PATH=/path/to/llvm-mingw-20250114-ucrt-ubuntu-20.04-x86_64/bin:$PATH

# FLAC - library only, no Ogg container, no command-line tools
tar xf flac-1.4.3.tar.xz && cd flac-1.4.3
./configure --host=x86_64-w64-mingw32 --enable-shared --disable-static \
            --disable-ogg --disable-programs --disable-examples \
            --disable-cpplibs --disable-doxygen-docs
make
# -> src/libFLAC/.libs/libFLAC-12.dll   (renamed to libFLAC.dll)

# LAME - library only
tar xf lame-3.100.tar.gz && cd lame-3.100
sed -i '/lame_init_old/d' include/libmp3lame.sym      # see below
./configure --host=x86_64-w64-mingw32 --enable-shared --disable-static --disable-frontend
make
# -> libmp3lame/.libs/libmp3lame-0.dll  (renamed to libmp3lame.dll)
```

Two things that will bite a rebuild:

- **`lame_init_old` must be removed from `include/libmp3lame.sym`.** LAME 3.100 lists that
  deprecated symbol for export but no longer defines it, so the link fails with
  `undefined symbol: lame_init_old`. Distro packagers patch the same line out.
- **Do NOT pass `--disable-decoder` to LAME.** It compiles out the `hip_*` decoder functions that
  the export list still names, and the link fails on eight more undefined symbols. The decoder costs
  a few KB and is simply unused.

The DLLs are renamed from their libtool `-12` / `-0` suffixes because `DllImport` resolves by the
name the module was loaded under, and `AudioNativeBootstrap` loads them by these filenames.

Add whatever lands here to the release checklist's dependency review, alongside
`dotnet list package --vulnerable --include-transitive`.
