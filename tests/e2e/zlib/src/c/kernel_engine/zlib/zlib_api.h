#ifndef ZLIB_API_H_
#define ZLIB_API_H_

#include <stddef.h>

typedef unsigned char Bytef;
typedef unsigned long uLong;

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Compresses a buffer.
 * @param dest [array_of:destLen] Output.
 * @param destLen [out] Written bytes.
 * @param source [array_of:sourceLen] Input.
 * @param sourceLen Input bytes.
 * @return [result] 0 on success.
 */
int compress(Bytef *dest, uLong *destLen, const Bytef *source, uLong sourceLen);

/**
 * Bound of a compressed size.
 * @param sourceLen Input bytes.
 * @return Worst case bytes.
 */
uLong compressBound(uLong sourceLen);

/** @return The version. */
const char *zlibVersion(void);

#ifdef __cplusplus
}
#endif

#endif
