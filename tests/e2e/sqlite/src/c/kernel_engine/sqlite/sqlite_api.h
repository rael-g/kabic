#ifndef SQLITE_API_H_
#define SQLITE_API_H_

#ifdef __cplusplus
extern "C" {
#endif

typedef struct sqlite3 sqlite3;

/**
 * Called once per row.
 * @param ctx [context] Opaque.
 * @param columns Column count.
 * @param values Column values.
 * @param names Column names.
 * @return 0 to go on.
 */
typedef int (*sqlite3_row_fn)(void *ctx, int columns, char **values, char **names);

/**
 * Opens a database.
 * @param filename Path.
 * @param ppDb [out] The database.
 * @return [result] 0 on success.
 */
int sqlite3_open(const char *filename, sqlite3 **ppDb);

/**
 * Closes a database.
 * @param db The database.
 * @return [result] 0 on success.
 */
int sqlite3_close(sqlite3 *db);

/**
 * Runs statements.
 * @param db The database.
 * @param sql Statements.
 * @param callback [closure:ctx] Per row.
 * @param ctx [context] Opaque.
 * @param errmsg [out] Error text.
 * @return [result] 0 on success.
 */
int sqlite3_exec(sqlite3 *db, const char *sql, sqlite3_row_fn callback, void *ctx, char **errmsg);

/** @return The library version. */
const char *sqlite3_libversion(void);

/**
 * The last error.
 * @param db The database.
 * @return Its message.
 */
const char *sqlite3_errmsg(sqlite3 *db);

#ifdef __cplusplus
}
#endif

#endif
