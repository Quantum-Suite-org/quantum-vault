/*
 * qvault_core.h - Quantum Vault Core Library - C ABI
 * Version: 0.1.0
 *
 * Public C ABI for qvault_core.dll.
 * Consumed by:
 *   - C# WPF GUI via P/Invoke
 *   - Python CLI via ctypes
 *
 * All strings are UTF-8, caller-owned unless noted.
 * Thread safety: single-writer, multiple-reader.
 * File locking is handled internally.
 */

#pragma once

#ifdef _WIN32
  #ifdef QVAULT_CORE_EXPORTS
    #define QVAPI __declspec(dllexport)
  #else
    #define QVAPI __declspec(dllimport)
  #endif
#else
  #define QVAPI
#endif

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------ */
/* Version                                                              */
/* ------------------------------------------------------------------ */

#define QVAULT_VERSION_MAJOR 0
#define QVAULT_VERSION_MINOR 1
#define QVAULT_VERSION_PATCH 0
#define QVAULT_VERSION_STR   "0.1.0"

QVAPI const char* qv_version(void);

/* ------------------------------------------------------------------ */
/* Error codes                                                          */
/* ------------------------------------------------------------------ */

typedef enum QVResult {
    QV_OK                   =  0,
    QV_ERR_NOT_INIT         = -1,
    QV_ERR_ALREADY_INIT     = -2,
    QV_ERR_BAD_ARG          = -3,
    QV_ERR_NOT_FOUND        = -4,
    QV_ERR_IO               = -5,
    QV_ERR_CORRUPT          = -6,
    QV_ERR_CRYPTO           = -7,
    QV_ERR_BAD_PASSWORD     = -8,
    QV_ERR_NO_PASSWORD      = -9,
    QV_ERR_LOCKED           = -10,
    QV_ERR_DUPLICATE        = -11,
    QV_ERR_TOO_LARGE        = -12,
    QV_ERR_INTERNAL         = -99,
} QVResult;

/* Returns a human-readable string for an error code. */
QVAPI const char* qv_error_string(QVResult code);

/* ------------------------------------------------------------------ */
/* Entry types                                                          */
/* ------------------------------------------------------------------ */

typedef enum QVEntryType {
    QV_TYPE_NOTE     = 0,
    QV_TYPE_SNIPPET  = 1,
    QV_TYPE_COMMAND  = 2,
} QVEntryType;

/* ------------------------------------------------------------------ */
/* Entry handle / struct                                                */
/* ------------------------------------------------------------------ */

/*
 * QVEntry is an opaque handle. Use accessor functions.
 * Lifetime: owned by the vault; valid until the next mutating call
 * or qv_entry_free() if obtained via qv_entry_clone().
 */
typedef struct QVEntry QVEntry;

/* Clone produces a caller-owned copy. Must call qv_entry_free(). */
QVAPI QVEntry*      qv_entry_clone(const QVEntry* e);
QVAPI void          qv_entry_free(QVEntry* e);

/* Accessors - returned strings valid for lifetime of entry handle */
QVAPI const char*   qv_entry_id(const QVEntry* e);
QVAPI QVEntryType   qv_entry_type(const QVEntry* e);
QVAPI const char*   qv_entry_title(const QVEntry* e);
QVAPI const char*   qv_entry_content(const QVEntry* e);   /* decrypted */
QVAPI const char*   qv_entry_tags(const QVEntry* e);      /* comma-separated */
QVAPI const char*   qv_entry_description(const QVEntry* e);
QVAPI const char*   qv_entry_language(const QVEntry* e);  /* for snippets/commands */
QVAPI int           qv_entry_is_favorite(const QVEntry* e);
QVAPI int           qv_entry_is_dpapi(const QVEntry* e);  /* notes: DPAPI flag */
QVAPI int64_t       qv_entry_created_at(const QVEntry* e);  /* unix timestamp */
QVAPI int64_t       qv_entry_updated_at(const QVEntry* e);

/* ------------------------------------------------------------------ */
/* Vault lifecycle                                                      */
/* ------------------------------------------------------------------ */

/*
 * qv_init: Initialize the vault.
 *   data_dir: UTF-8 path to data directory. If NULL, uses default
 *             (%APPDATA%\Quantum-suite-org\QuantumVault).
 *   log_dir:  UTF-8 path to log directory. If NULL, uses default.
 *
 * Must be called before any other qv_ function (except qv_version).
 * Safe to call again after qv_shutdown().
 */
QVAPI QVResult qv_init(const char* data_dir, const char* log_dir);

/*
 * qv_shutdown: Flush, close file handles, release memory.
 * Safe to call multiple times.
 */
QVAPI void qv_shutdown(void);

/*
 * qv_is_first_launch: Returns 1 if no vault data file found yet.
 */
QVAPI int qv_is_first_launch(void);

/* ------------------------------------------------------------------ */
/* Notes password                                                       */
/* ------------------------------------------------------------------ */

/*
 * qv_notes_set_password: Set the master password for this session.
 *   password: UTF-8 password string.
 *
 * Must be called before reading/writing note content.
 * On first call ever, this password becomes the master.
 * On subsequent launches, must match the stored salt+verify token.
 */
QVAPI QVResult qv_notes_set_password(const char* password);

/*
 * qv_notes_is_unlocked: Returns 1 if password has been set this session.
 */
QVAPI int qv_notes_is_unlocked(void);

/*
 * qv_notes_change_password: Change the master password.
 *   old_password: current password for verification.
 *   new_password: new password.
 * Re-encrypts all notes with the new key.
 */
QVAPI QVResult qv_notes_change_password(const char* old_password,
                                         const char* new_password);

/* ------------------------------------------------------------------ */
/* CRUD                                                                 */
/* ------------------------------------------------------------------ */

/*
 * qv_entry_create: Create a new entry.
 *   type:        QVEntryType
 *   title:       UTF-8 title (required, max 512 bytes)
 *   content:     UTF-8 content (plaintext; encrypted at rest)
 *   tags:        comma-separated tags, may be NULL
 *   description: short description, may be NULL
 *   language:    programming language hint, may be NULL
 *   use_dpapi:   1 to enable DPAPI wrap for notes (ignored for non-notes)
 *   out_id:      receives newly-assigned ID string (caller must NOT free;
 *                valid until next mutating call)
 */
QVAPI QVResult qv_entry_create(
    QVEntryType  type,
    const char*  title,
    const char*  content,
    const char*  tags,
    const char*  description,
    const char*  language,
    int          use_dpapi,
    const char** out_id
);

/*
 * qv_entry_update: Update fields of an existing entry.
 *   Any pointer field set to NULL means "do not change that field".
 *   use_dpapi: -1 = don't change, 0 = disable, 1 = enable
 */
QVAPI QVResult qv_entry_update(
    const char* id,
    const char* title,
    const char* content,
    const char* tags,
    const char* description,
    const char* language,
    int         use_dpapi
);

QVAPI QVResult qv_entry_delete(const char* id);

/*
 * qv_entry_duplicate: Create a copy of an entry with a new ID.
 *   " (copy)" is appended to the title.
 *   out_id: receives new entry ID.
 */
QVAPI QVResult qv_entry_duplicate(const char* id, const char** out_id);

QVAPI QVResult qv_entry_set_favorite(const char* id, int is_favorite);

/* ------------------------------------------------------------------ */
/* Retrieval                                                            */
/* ------------------------------------------------------------------ */

/*
 * qv_entry_get: Get a single entry by ID.
 *   out_entry: receives a pointer into vault-owned memory.
 *              Do NOT free. Clone if you need to keep it past the next
 *              mutating call.
 */
QVAPI QVResult qv_entry_get(const char* id, const QVEntry** out_entry);

/*
 * qv_list_result: Result set from list/search operations.
 * Call qv_list_result_free() when done.
 */
typedef struct {
    QVEntry** entries;  /* array of vault-owned pointers */
    int       count;
} QVListResult;

QVAPI void qv_list_result_free(QVListResult* r);

/*
 * qv_entry_list: List entries.
 *   type:          -1 = all types
 *   favorites_only: 1 = only favorites
 *   limit:         0 = no limit
 *   offset:        pagination offset
 *   sort_by:       "created", "updated", "title" (default: "updated")
 */
QVAPI QVResult qv_entry_list(
    int           type,
    int           favorites_only,
    int           limit,
    int           offset,
    const char*   sort_by,
    QVListResult* out
);

/*
 * qv_entry_search: Full-text search across title, tags, description.
 *   Content of notes is NOT searched (encrypted at rest; would require
 *   password + decryption of all notes - expensive, opt-in via
 *   search_content flag).
 *   type: -1 = all types
 */
QVAPI QVResult qv_entry_search(
    const char*   query,
    int           type,
    int           search_content,
    int           limit,
    QVListResult* out
);

/* ------------------------------------------------------------------ */
/* Recently edited                                                      */
/* ------------------------------------------------------------------ */

QVAPI QVResult qv_entry_recent(int limit, QVListResult* out);

/* ------------------------------------------------------------------ */
/* Import / Export                                                      */
/* ------------------------------------------------------------------ */

/*
 * qv_export_json: Export all entries to a JSON file.
 *   path:          destination file path (UTF-8)
 *   include_encrypted: 1 = decrypt and include note content in export
 *                      (requires password to be set)
 *                  0 = export notes with content as empty string
 */
QVAPI QVResult qv_export_json(const char* path, int include_encrypted);

/*
 * qv_import_json: Import entries from a JSON file.
 *   path:         source file path (UTF-8)
 *   merge:        1 = merge with existing data (skip duplicate IDs)
 *                 0 = replace all data (after confirmation)
 *   out_imported: receives count of entries imported
 *   out_skipped:  receives count skipped (duplicates)
 *   out_errors:   receives count of malformed entries skipped
 */
QVAPI QVResult qv_import_json(
    const char* path,
    int         merge,
    int*        out_imported,
    int*        out_skipped,
    int*        out_errors
);

/* ------------------------------------------------------------------ */
/* Backup                                                               */
/* ------------------------------------------------------------------ */

/*
 * qv_backup: Create a timestamped backup of the vault data file.
 *   backup_dir: if NULL, uses data_dir/backups/
 *   out_path:   receives path of created backup file (valid until
 *               next qv_ call; do not free)
 */
QVAPI QVResult qv_backup(const char* backup_dir, const char** out_path);

/* ------------------------------------------------------------------ */
/* Stats / Diagnostics                                                  */
/* ------------------------------------------------------------------ */

typedef struct {
    int total_entries;
    int note_count;
    int snippet_count;
    int command_count;
    int favorite_count;
    int64_t vault_file_size_bytes;
} QVStats;

QVAPI QVResult qv_get_stats(QVStats* out);

/*
 * qv_get_data_dir: Get the resolved data directory path.
 * Return value valid until qv_shutdown().
 */
QVAPI const char* qv_get_data_dir(void);

/*
 * qv_get_log_dir: Get the resolved log directory path.
 */
QVAPI const char* qv_get_log_dir(void);

#ifdef __cplusplus
}
#endif
