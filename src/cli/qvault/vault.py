"""
qvault - Quantum Vault CLI
Calls qvault_core.dll via ctypes C ABI.
"""
import ctypes
import ctypes.util
import os
import sys
import json
import platform

# ------------------------------------------------------------------ #
# DLL loading
# ------------------------------------------------------------------ #

def _find_dll():
    """Locate qvault_core.dll relative to this script or on PATH."""
    candidates = [
        os.path.join(os.path.dirname(__file__), "qvault_core.dll"),
        os.path.join(os.path.dirname(__file__), "..", "..", "build", "bin", "qvault_core.dll"),
        os.path.join(os.path.dirname(sys.executable), "qvault_core.dll"),
    ]
    for p in candidates:
        p = os.path.normpath(p)
        if os.path.isfile(p):
            return p
    # Try PATH
    found = ctypes.util.find_library("qvault_core")
    if found:
        return found
    raise FileNotFoundError(
        "qvault_core.dll not found. Ensure it is in the same directory as qvault.exe "
        "or in the Quantum Vault installation directory."
    )

_dll = None

def _get_dll():
    global _dll
    if _dll is None:
        path = _find_dll()
        _dll = ctypes.CDLL(path)
        _setup_dll(_dll)
    return _dll

# ------------------------------------------------------------------ #
# C type definitions
# ------------------------------------------------------------------ #

class QVListResult(ctypes.Structure):
    _fields_ = [
        ("entries", ctypes.POINTER(ctypes.c_void_p)),
        ("count",   ctypes.c_int),
    ]

class QVStats(ctypes.Structure):
    _fields_ = [
        ("total_entries",       ctypes.c_int),
        ("note_count",          ctypes.c_int),
        ("snippet_count",       ctypes.c_int),
        ("command_count",       ctypes.c_int),
        ("favorite_count",      ctypes.c_int),
        ("vault_file_size_bytes", ctypes.c_int64),
    ]

QV_OK            =  0
QV_ERR_NOT_FOUND = -4
QV_ERR_NO_PASSWORD = -9

TYPE_NOTE    = 0
TYPE_SNIPPET = 1
TYPE_COMMAND = 2

TYPE_NAMES = {TYPE_NOTE: "note", TYPE_SNIPPET: "snippet", TYPE_COMMAND: "command"}
TYPE_FROM_STR = {"note": TYPE_NOTE, "snippet": TYPE_SNIPPET, "command": TYPE_COMMAND}

def _utf8(s):
    if s is None:
        return None
    return s.encode("utf-8")

def _str(ptr):
    if not ptr:
        return ""
    return ctypes.cast(ptr, ctypes.c_char_p).value.decode("utf-8", errors="replace")

def _setup_dll(lib):
    # qv_version
    lib.qv_version.restype  = ctypes.c_char_p
    lib.qv_version.argtypes = []

    # qv_error_string
    lib.qv_error_string.restype  = ctypes.c_char_p
    lib.qv_error_string.argtypes = [ctypes.c_int]

    # qv_init
    lib.qv_init.restype  = ctypes.c_int
    lib.qv_init.argtypes = [ctypes.c_char_p, ctypes.c_char_p]

    # qv_shutdown
    lib.qv_shutdown.restype  = None
    lib.qv_shutdown.argtypes = []

    # qv_is_first_launch
    lib.qv_is_first_launch.restype  = ctypes.c_int
    lib.qv_is_first_launch.argtypes = []

    # qv_notes_set_password
    lib.qv_notes_set_password.restype  = ctypes.c_int
    lib.qv_notes_set_password.argtypes = [ctypes.c_char_p]

    # qv_notes_is_unlocked
    lib.qv_notes_is_unlocked.restype  = ctypes.c_int
    lib.qv_notes_is_unlocked.argtypes = []

    # qv_entry_create
    lib.qv_entry_create.restype  = ctypes.c_int
    lib.qv_entry_create.argtypes = [
        ctypes.c_int,    # type
        ctypes.c_char_p, # title
        ctypes.c_char_p, # content
        ctypes.c_char_p, # tags
        ctypes.c_char_p, # description
        ctypes.c_char_p, # language
        ctypes.c_int,    # use_dpapi
        ctypes.POINTER(ctypes.c_char_p), # out_id
    ]

    # qv_entry_delete
    lib.qv_entry_delete.restype  = ctypes.c_int
    lib.qv_entry_delete.argtypes = [ctypes.c_char_p]

    # qv_entry_get
    lib.qv_entry_get.restype  = ctypes.c_int
    lib.qv_entry_get.argtypes = [ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]

    # qv_entry_list
    lib.qv_entry_list.restype  = ctypes.c_int
    lib.qv_entry_list.argtypes = [
        ctypes.c_int,    # type (-1=all)
        ctypes.c_int,    # favorites_only
        ctypes.c_int,    # limit
        ctypes.c_int,    # offset
        ctypes.c_char_p, # sort_by
        ctypes.POINTER(QVListResult),
    ]

    # qv_list_result_free
    lib.qv_list_result_free.restype  = None
    lib.qv_list_result_free.argtypes = [ctypes.POINTER(QVListResult)]

    # qv_entry_search
    lib.qv_entry_search.restype  = ctypes.c_int
    lib.qv_entry_search.argtypes = [
        ctypes.c_char_p, # query
        ctypes.c_int,    # type
        ctypes.c_int,    # search_content
        ctypes.c_int,    # limit
        ctypes.POINTER(QVListResult),
    ]

    # qv_entry_set_favorite
    lib.qv_entry_set_favorite.restype  = ctypes.c_int
    lib.qv_entry_set_favorite.argtypes = [ctypes.c_char_p, ctypes.c_int]

    # entry accessors
    for name, rtype in [
        ("qv_entry_id",          ctypes.c_char_p),
        ("qv_entry_type",        ctypes.c_int),
        ("qv_entry_title",       ctypes.c_char_p),
        ("qv_entry_content",     ctypes.c_char_p),
        ("qv_entry_tags",        ctypes.c_char_p),
        ("qv_entry_description", ctypes.c_char_p),
        ("qv_entry_language",    ctypes.c_char_p),
        ("qv_entry_is_favorite", ctypes.c_int),
        ("qv_entry_is_dpapi",    ctypes.c_int),
        ("qv_entry_created_at",  ctypes.c_int64),
        ("qv_entry_updated_at",  ctypes.c_int64),
    ]:
        fn = getattr(lib, name)
        fn.restype  = rtype
        fn.argtypes = [ctypes.c_void_p]

    # export / import
    lib.qv_export_json.restype  = ctypes.c_int
    lib.qv_export_json.argtypes = [ctypes.c_char_p, ctypes.c_int]

    lib.qv_import_json.restype  = ctypes.c_int
    lib.qv_import_json.argtypes = [
        ctypes.c_char_p, ctypes.c_int,
        ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int)
    ]

    # backup
    lib.qv_backup.restype  = ctypes.c_int
    lib.qv_backup.argtypes = [ctypes.c_char_p, ctypes.POINTER(ctypes.c_char_p)]

    # stats
    lib.qv_get_stats.restype  = ctypes.c_int
    lib.qv_get_stats.argtypes = [ctypes.POINTER(QVStats)]

    # paths
    lib.qv_get_data_dir.restype  = ctypes.c_char_p
    lib.qv_get_data_dir.argtypes = []
    lib.qv_get_log_dir.restype   = ctypes.c_char_p
    lib.qv_get_log_dir.argtypes  = []

# ------------------------------------------------------------------ #
# Vault context manager
# ------------------------------------------------------------------ #

class Vault:
    def __init__(self, data_dir=None):
        self._lib = _get_dll()
        r = self._lib.qv_init(
            _utf8(data_dir) if data_dir else None,
            None
        )
        if r not in (QV_OK, -2):  # OK or AlreadyInit
            err = self._lib.qv_error_string(r).decode()
            raise RuntimeError(f"Failed to initialize vault: {err} (code {r})")

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self._lib.qv_shutdown()

    def version(self):
        return self._lib.qv_version().decode()

    def error_string(self, code):
        return self._lib.qv_error_string(code).decode()

    def _read_entry(self, ptr):
        lib = self._lib
        return {
            "id":          lib.qv_entry_id(ptr).decode("utf-8", errors="replace"),
            "type":        lib.qv_entry_type(ptr),
            "type_name":   TYPE_NAMES.get(lib.qv_entry_type(ptr), "unknown"),
            "title":       lib.qv_entry_title(ptr).decode("utf-8", errors="replace"),
            "content":     lib.qv_entry_content(ptr).decode("utf-8", errors="replace"),
            "tags":        lib.qv_entry_tags(ptr).decode("utf-8", errors="replace"),
            "description": lib.qv_entry_description(ptr).decode("utf-8", errors="replace"),
            "language":    lib.qv_entry_language(ptr).decode("utf-8", errors="replace"),
            "is_favorite": bool(lib.qv_entry_is_favorite(ptr)),
            "is_dpapi":    bool(lib.qv_entry_is_dpapi(ptr)),
            "created_at":  lib.qv_entry_created_at(ptr),
            "updated_at":  lib.qv_entry_updated_at(ptr),
        }

    def list_entries(self, type_filter=-1, favorites_only=False, limit=0,
                     offset=0, sort_by="updated"):
        result = QVListResult()
        r = self._lib.qv_entry_list(
            type_filter, 1 if favorites_only else 0,
            limit, offset, _utf8(sort_by),
            ctypes.byref(result)
        )
        if r != QV_OK:
            raise RuntimeError(self.error_string(r))
        entries = []
        for i in range(result.count):
            ptr = result.entries[i]
            if ptr:
                entries.append(self._read_entry(ptr))
        self._lib.qv_list_result_free(ctypes.byref(result))
        return entries

    def search(self, query, type_filter=-1, search_content=False, limit=100):
        result = QVListResult()
        r = self._lib.qv_entry_search(
            _utf8(query), type_filter,
            1 if search_content else 0, limit,
            ctypes.byref(result)
        )
        if r != QV_OK:
            raise RuntimeError(self.error_string(r))
        entries = []
        for i in range(result.count):
            ptr = result.entries[i]
            if ptr:
                entries.append(self._read_entry(ptr))
        self._lib.qv_list_result_free(ctypes.byref(result))
        return entries

    def get(self, entry_id):
        ptr = ctypes.c_void_p(0)
        r = self._lib.qv_entry_get(_utf8(entry_id), ctypes.byref(ptr))
        if r != QV_OK:
            raise RuntimeError(self.error_string(r))
        return self._read_entry(ptr)

    def add(self, entry_type, title, content="", tags="",
            description="", language="", use_dpapi=False):
        out_id = ctypes.c_char_p(None)
        r = self._lib.qv_entry_create(
            entry_type,
            _utf8(title), _utf8(content), _utf8(tags),
            _utf8(description), _utf8(language),
            1 if use_dpapi else 0,
            ctypes.byref(out_id)
        )
        if r != QV_OK:
            raise RuntimeError(self.error_string(r))
        return out_id.value.decode()

    def delete(self, entry_id):
        r = self._lib.qv_entry_delete(_utf8(entry_id))
        if r != QV_OK:
            raise RuntimeError(self.error_string(r))

    def export_json(self, path, include_content=True):
        r = self._lib.qv_export_json(_utf8(path), 1 if include_content else 0)
        if r != QV_OK:
            raise RuntimeError(self.error_string(r))

    def import_json(self, path, merge=True):
        imported = ctypes.c_int(0)
        skipped  = ctypes.c_int(0)
        errors   = ctypes.c_int(0)
        r = self._lib.qv_import_json(
            _utf8(path), 1 if merge else 0,
            ctypes.byref(imported), ctypes.byref(skipped), ctypes.byref(errors)
        )
        if r != QV_OK:
            raise RuntimeError(self.error_string(r))
        return imported.value, skipped.value, errors.value

    def backup(self, backup_dir=None):
        out_path = ctypes.c_char_p(None)
        r = self._lib.qv_backup(
            _utf8(backup_dir) if backup_dir else None,
            ctypes.byref(out_path)
        )
        if r != QV_OK:
            raise RuntimeError(self.error_string(r))
        return out_path.value.decode() if out_path.value else ""

    def stats(self):
        s = QVStats()
        r = self._lib.qv_get_stats(ctypes.byref(s))
        if r != QV_OK:
            raise RuntimeError(self.error_string(r))
        return {
            "total_entries":        s.total_entries,
            "note_count":           s.note_count,
            "snippet_count":        s.snippet_count,
            "command_count":        s.command_count,
            "favorite_count":       s.favorite_count,
            "vault_file_size_bytes": s.vault_file_size_bytes,
        }

    def data_dir(self):
        return self._lib.qv_get_data_dir().decode()

    def set_password(self, password):
        r = self._lib.qv_notes_set_password(_utf8(password))
        return r

    def is_notes_unlocked(self):
        return bool(self._lib.qv_notes_is_unlocked())

    def copy_to_clipboard(self, text):
        """Copy text to Windows clipboard."""
        try:
            import subprocess
            proc = subprocess.Popen(["clip"], stdin=subprocess.PIPE, shell=True)
            proc.communicate(input=text.encode("utf-16-le"))
            return True
        except Exception:
            return False
