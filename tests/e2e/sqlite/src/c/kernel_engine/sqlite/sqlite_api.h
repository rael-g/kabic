#ifndef SQLITE_API_H_
#define SQLITE_API_H_

#ifdef __cplusplus
extern "C" {
#endif

typedef struct sqlite3 sqlite3;

/**
 * Opens a database.
 * @param filename Path.
 * @param ppDb The database.
 * @return 0 on success.
 */
int sqlite3_open(const char *filename, sqlite3 **ppDb);

/**
 * Closes a database.
 * @param db The database.
 * @return 0 on success.
 */
int sqlite3_close(sqlite3 *db);

/**
 * Runs statements.
 * @param db The database.
 * @param sql Statements.
 * @param callback Per row.
 * @param errmsg Error text.
 * @return 0 on success.
 */
int sqlite3_exec(sqlite3 *db, const char *sql, int (*callback)(void *, int, char **, char **), void *, char **errmsg);

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
