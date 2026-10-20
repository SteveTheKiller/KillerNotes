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
| `flac-1.5.0.tar.xz` | `downloads.xiph.org/releases/flac/` | `f2c1c76592a82ffff8413ba3c4a1299b6c7ab06c734dee03fd88630485c2b920` |
| `lame-3.100.tar.gz` | `downloads.sourceforge.net/project/lame/lame/3.100/` | `ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e` |

Both match the hashes upstream publishes for those releases.

### Built binaries

| DLL | Version | SHA-256 |
|---|---|---|
| `libFLAC.dll` | 1.5.0 | `4fc103d5fd784797fc1ae04ccdbb86068ff8e6b2980e4649927fa72e6263737e` |
| `libmp3lame.dll` | 3.100 | `0dd9ff2134da0664efa7434fe8ec20ff85ad36daf7296bd7aabcc1e3abefbaa3` |

Both PE32+ x64, stripped. Verify with `sha256sum` before a release; if either changes without a
deliberate rebuild, something is wrong.

### Reproducing the build

FLAC uses `llvm-mingw` 20260616 (ucrt, x86_64); LAME retains its 20250114 build.
Both toolchains come from their upstream GitHub releases.

```sh
export PATH=/path/to/llvm-mingw-20260616-ucrt-ubuntu-22.04-x86_64/bin:$PATH

# FLAC - library only, no Ogg container, no command-line tools
tar xf flac-1.5.0.tar.xz && cd flac-1.5.0
./configure --build=x86_64-pc-linux-gnu --host=x86_64-w64-mingw32 --enable-shared --disable-static \
            --disable-ogg --disable-programs --disable-examples \
            --disable-cpplibs --disable-doxygen-docs --disable-multithreading
make
# Strip src/libFLAC/.libs/libFLAC-14.dll and rename it to libFLAC.dll.

# LAME - library only
export PATH=/path/to/llvm-mingw-20250114-ucrt-ubuntu-20.04-x86_64/bin:$PATH
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

## FLAC verification, October 20, 2026

The 1.5.0 Windows x64 build preserves byte-exact PCM through the production encoder and
decoder at 16 kHz mono, 44.1 kHz stereo and 48 kHz mono. It also decodes a recording made
by the previous 1.4.3 DLL without changing its samples. Multithreading remains disabled,
preserving the previous single-threaded behavior. The old source archive remains as
legacy provenance. This is an unsigned development rebuild; signing, packaging and real
hardware recording checks remain release gates.

## Offline speech recognition

The four MIT-licensed speech DLLs are built together from whisper.cpp 1.9.5,
commit `d1be6fde11ac6e0407606b4e42fe72d34add8037`. The retained source archive
`whisper.cpp-1.9.5.tar.gz` comes from
`https://codeload.github.com/ggml-org/whisper.cpp/tar.gz/d1be6fde11ac6e0407606b4e42fe72d34add8037`.
Its locally calculated SHA-256 is
`645445da884df6dbd52b30f82d7be7c01d1767c938714e477e786b88d529e9f4`.
The previous 1.7.4 archive remains for provenance.

| DLL | SHA-256 |
|---|---|
| `ggml-base.dll` | `a228f0da665455eea98c1826539e28bf0f9045584c97205e06e80a14d6d0ad42` |
| `ggml-cpu.dll` | `9e6b13528dd85f25bff7cf888ddcec49a440c49cbae0b85a3b6e23d6c1976875` |
| `ggml.dll` | `3782042c887cc96f7b494765ffceece482a1cc8fc0704e95e7533d1ea643cbda` |
| `libwhisper.dll` | `5ee14bcc32c4ffeea8628824a14a98304c59df65c6742b17fd5173b3f7b3f537` |

Rebuild on Linux with CMake 4.2.3, make and llvm-mingw 20260616 (ucrt, x86_64).
Extract the archive into a scratch directory, then apply `whisper-windows-utf8.patch`
inside its source directory with `git apply`. This patch enables Windows wide-path
loading under MinGW and preserves UTF-16 surrogate pairs. From that source directory:

```sh
export PATH=/path/to/llvm-mingw-20260616-ucrt-ubuntu-22.04-x86_64/bin:$PATH
cmake -S . -B ../whisper-build -G 'Unix Makefiles' \
  -DCMAKE_SYSTEM_NAME=Windows -DCMAKE_SYSTEM_PROCESSOR=AMD64 \
  -DCMAKE_C_COMPILER=x86_64-w64-mingw32-clang \
  -DCMAKE_CXX_COMPILER=x86_64-w64-mingw32-clang++ \
  -DCMAKE_RC_COMPILER=x86_64-w64-mingw32-windres \
  -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON -DWHISPER_BUILD_IS_DEV=OFF \
  -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF -DGGML_CUDA=OFF -DGGML_VULKAN=OFF \
  -DGGML_BLAS=OFF -DGGML_BACKEND_DL=OFF \
  -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=OFF -DWHISPER_CURL=OFF \
  '-DCMAKE_SHARED_LINKER_FLAGS=-static-libgcc -static-libstdc++'
cmake --build ../whisper-build -j4
# Copy the four DLLs from the build tree, then llvm-strip each copy.
```

The managed interop requires native version 1.9.5. Native initialization and parameter
checks run on .NET Framework 4.8. A full transcription of the upstream JFK sample
with the author's tiny.en model also passes from a non-English folder name. These
are unsigned development binaries; packaging, signing and microphone checks remain
release gates.
