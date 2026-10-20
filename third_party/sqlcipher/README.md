# e_sqlcipher.dll - vendored SQLCipher native (x64)

Replaces the deprecated `SQLitePCLRaw.lib.e_sqlcipher` NuGet package, whose entire line
was marked "legacy, no longer maintained" by its author in 2025. Building from upstream
source keeps us on current SQLCipher releases; the `SQLitePCLRaw.provider.e_sqlcipher`
package (a managed P/Invoke shim) still loads this DLL by name exactly as before.

The recipe reproduces SQLitePCL.raw's own build (github.com/ericsink/cb): the SQLCipher
amalgamation compiled with the LibTomCrypt crypto provider and AES-256-CBC, so the file
format, KDF (PBKDF2-HMAC-SHA512, 256000 iterations) and defaults are identical -
databases created under the old package open unchanged. The same DLL is vendored in
Killendar's and KillerShot's `third_party/sqlcipher/`; rebuild once, copy to all three, keep
the hashes equal.

## Current binary

| | |
|---|---|
| SQLCipher | 4.19.0 community (SQLite 3.53.4) |
| LibTomCrypt | 1.18.2 |
| Toolchain | llvm-mingw 20260616 (clang 22.1.8), ucrt |
| e_sqlcipher.dll sha256 | `ec67d36a1e01d9e68726319d88e6d3a9b294fa3ad52e71555c2a120f6c234a4a` |
| Signature | Unsigned development rebuild. Signing and timestamping remain a separately authorized release step. |

Source tarballs (verify before building):

| Tarball | URL | sha256 |
|---|---|---|
| sqlcipher v4.19.0 | github.com/sqlcipher/sqlcipher/archive/refs/tags/v4.19.0.tar.gz | `7075f96cbabe45b4ecfc2e6b1745a625f856f695b0827a5506ce9ed85b906aa0` |
| libtomcrypt 1.18.2 | github.com/libtom/libtomcrypt/releases/download/v1.18.2/crypt-1.18.2.tar.xz | `96ad4c3b8336050993c5bc2cf6c057484f2b0f9f763448151567fbab5e767b84` |
| llvm-mingw 20260616 | github.com/mstorsjo/llvm-mingw/releases/download/20260616/llvm-mingw-20260616-ucrt-ubuntu-22.04-x86_64.tar.xz | `534b92e067b22a6b4441f48ae9240a3341b17825d04d577eab0cf85c44b4deda` |

## Dependency verification, October 20, 2026

The DLL matches KillerShot's 4.19.0 development rebuild by SHA-256. Separate Windows
processes created an encrypted FTS5 database using 4.18.0, then opened and searched it
using this DLL. The content was preserved, the header remained encrypted, a wrong
password returned SQLITE_NOTADB, and cipher_version reported 4.19.0 community.

[SQLCipher 4.19.0](https://www.zetetic.net/blog/2026/09/08/sqlcipher-4.19.0-release/)
fixes export alias quoting and invalid hexkey URI handling. Signing, timestamping,
installer validation and final release review remain open.

## Build (Linux host, cross-compiles to Windows x64)

Extract the three tarballs into one folder. Generate the amalgamation first
(needs tclsh):

```sh
cd sqlcipher-4.19.0 && ./configure --with-tempstore=yes && make sqlite3.c && cd ..
```

Then run `build-e_sqlcipher.sh` (in this folder). It compiles the amalgamation plus the
34 LibTomCrypt files SQLCipher's libtomcrypt provider needs, with the same define set
SQLitePCL.raw used, and strips the result.

## Verification checklist (repeat on every rebuild)

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
