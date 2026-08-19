#!/bin/sh
# Builds e_sqlcipher.dll (x64) from source, replacing the deprecated
# SQLitePCLRaw.lib.e_sqlcipher package. Same recipe as SQLitePCL.raw's own
# builds (github.com/ericsink/cb): SQLCipher amalgamation + LibTomCrypt
# provider, AES-256-CBC, exports via SQLITE_API=dllexport.
# See README.md in this folder for tarball URLs, hashes, and the
# amalgamation-generation step that must run first.
set -e
TC=./llvm-mingw-20260616-ucrt-ubuntu-22.04-x86_64/bin
SC=./sqlcipher-4.18.0
LTC=./libtomcrypt-1.18.2/src

LTC_FILES="
modes/cbc/cbc_decrypt.c modes/cbc/cbc_done.c modes/cbc/cbc_encrypt.c
modes/cbc/cbc_getiv.c modes/cbc/cbc_setiv.c modes/cbc/cbc_start.c
prngs/fortuna.c prngs/rng_get_bytes.c
mac/hmac/hmac_done.c mac/hmac/hmac_file.c mac/hmac/hmac_init.c
mac/hmac/hmac_memory.c mac/hmac/hmac_memory_multi.c mac/hmac/hmac_process.c
hashes/sha2/sha256.c hashes/sha2/sha512.c hashes/sha1.c
hashes/helper/hash_memory.c
ciphers/aes/aes.c
misc/crypt/crypt_argchk.c misc/crypt/crypt_hash_is_valid.c
misc/crypt/crypt_hash_descriptor.c misc/crypt/crypt_cipher_descriptor.c
misc/crypt/crypt_cipher_is_valid.c misc/crypt/crypt_find_cipher.c
misc/crypt/crypt_register_hash.c misc/crypt/crypt_register_cipher.c
misc/crypt/crypt_find_hash.c misc/crypt/crypt_register_prng.c
misc/crypt/crypt_prng_descriptor.c
misc/zeromem.c misc/compare_testvector.c misc/pkcs5/pkcs_5_2.c
"
SRCS="$SC/sqlite3.c"
for f in $LTC_FILES; do SRCS="$SRCS $LTC/$f"; done

$TC/x86_64-w64-mingw32-clang -shared -O2 \
  -o e_sqlcipher.dll $SRCS \
  -I$SC -I$LTC/headers \
  -DENDIAN_LITTLE -DLTC_NO_PROTOTYPES -DLTC_SOURCE \
  -DSQLITE_HAS_CODEC -DSQLITE_TEMP_STORE=2 \
  -DSQLCIPHER_CRYPTO_LIBTOMCRYPT '-DCIPHER="AES-256-CBC"' \
  -DSQLITE_ENABLE_COLUMN_METADATA -DSQLITE_ENABLE_FTS3_PARENTHESIS \
  -DSQLITE_ENABLE_FTS4 -DSQLITE_ENABLE_FTS5 -DSQLITE_ENABLE_JSON1 \
  -DSQLITE_ENABLE_MATH_FUNCTIONS -DSQLITE_ENABLE_RTREE \
  -DSQLITE_ENABLE_SNAPSHOT -DSQLITE_DEFAULT_FOREIGN_KEYS=1 \
  -DSQLITE_OS_WIN -DSQLITE_WIN32_FILEMAPPING_API=1 \
  '-DSQLITE_API=__declspec(dllexport)' \
  -DSQLITE_EXTRA_INIT=sqlcipher_extra_init -DSQLITE_EXTRA_SHUTDOWN=sqlcipher_extra_shutdown \
  -ladvapi32 -lbcrypt
$TC/llvm-strip e_sqlcipher.dll
