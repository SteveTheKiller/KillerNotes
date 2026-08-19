# e_sqlcipher.dll - vendored SQLCipher native (x64)

Replaces the deprecated `SQLitePCLRaw.lib.e_sqlcipher` NuGet package, whose entire line
was marked "legacy, no longer maintained" by its author in 2025. Building from upstream
source keeps us on current SQLCipher releases; the `SQLitePCLRaw.provider.e_sqlcipher`
package (a managed P/Invoke shim) still loads this DLL by name exactly as before.

The recipe reproduces SQLitePCL.raw's own build (github.com/ericsink/cb): the SQLCipher
amalgamation compiled with the LibTomCrypt crypto provider and AES-256-CBC, so the file
format, KDF (PBKDF2-HMAC-SHA512, 256000 iterations) and defaults are identical -
databases created under the old package open unchanged. The same DLL is vendored in
Killendar's `third_party/sqlcipher/`; rebuild once, copy to both, keep the hashes equal.

## Current binary

| | |
|---|---|
| SQLCipher | 4.18.0 community (SQLite 3.53.4) |
| LibTomCrypt | 1.18.2 |
| Toolchain | llvm-mingw 20260616 (clang 22.1.8), ucrt |
| e_sqlcipher.dll sha256 | `e327c328c633e5ec36aabe4ca11a55ab280d05b8940632d692f574ff6e3ed695` |

Source tarballs (verify before building):

| Tarball | URL | sha256 |
|---|---|---|
| sqlcipher v4.18.0 | github.com/sqlcipher/sqlcipher/archive/refs/tags/v4.18.0.tar.gz | `1df02d1b346fa27feaf2da2cb2c0d8209e788248e461ec288718aa5d3e9643e5` |
| libtomcrypt 1.18.2 | github.com/libtom/libtomcrypt/releases/download/v1.18.2/crypt-1.18.2.tar.xz | `96ad4c3b8336050993c5bc2cf6c057484f2b0f9f763448151567fbab5e767b84` |
| llvm-mingw 20260616 | github.com/mstorsjo/llvm-mingw/releases/download/20260616/llvm-mingw-20260616-ucrt-ubuntu-22.04-x86_64.tar.xz | `534b92e067b22a6b4441f48ae9240a3341b17825d04d577eab0cf85c44b4deda` |

## Build (Linux host, cross-compiles to Windows x64)

Extract the three tarballs into one folder. Generate the amalgamation first
(needs tclsh):

```sh
cd sqlcipher-4.18.0 && ./configure --with-tempstore=yes && make sqlite3.c && cd ..
```

Then run `build-e_sqlcipher.sh` (in this folder). It compiles the amalgamation plus the
34 LibTomCrypt files SQLCipher's libtomcrypt provider needs, with the same define set
SQLitePCL.raw used, and strips the result.

## Verification checklist (done for the current binary, repeat on every rebuild)

- PE32+ x64 DLL; imports limited to KERNEL32, ADVAPI32 and ucrt api-sets
- Exports include sqlite3_key, sqlite3_key_v2, sqlite3_rekey, sqlite3_open_v2
- `PBKDF2_ITER` in src/sqlcipher.c still 256000, or existing databases stop opening
- A native build of the identical sources passes a round-trip smoke test: FTS5 MATCH
  works, the file header is not plaintext, a wrong key fails with SQLITE_NOTADB,
  `PRAGMA cipher_version` reports the expected version
- On Windows: open an existing encrypted database, run a search, change the password

## Licenses

SQLCipher: BSD-style (Zetetic LLC). LibTomCrypt 1.18.2: dual WTFPL/public domain.
Both are GPLv3-compatible. This is SQLCipher Community Edition built unofficially;
it is not a Zetetic-supported binary.
