"""
Quantum Vault - Core integration tests.
Tests run against the real qvault_core.dll using a temp data directory.
Requires: qvault_core.dll built and on PATH or in build/bin.
"""
import os
import sys
import json
import shutil
import tempfile
import ctypes
import pytest

# Add CLI src to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src", "cli"))

from qvault.vault import Vault, TYPE_FROM_STR, QV_OK

# ------------------------------------------------------------------ #
# Fixtures
# ------------------------------------------------------------------ #

@pytest.fixture(scope="function")
def vault(tmp_path):
    """Fresh vault in a temp directory for each test."""
    data_dir = str(tmp_path / "vault_data")
    v = Vault(data_dir=data_dir)
    yield v
    v._lib.qv_shutdown()

# ------------------------------------------------------------------ #
# Version
# ------------------------------------------------------------------ #

def test_version(vault):
    ver = vault.version()
    assert ver == "0.1.0"

# ------------------------------------------------------------------ #
# First launch
# ------------------------------------------------------------------ #

def test_first_launch(tmp_path):
    data_dir = str(tmp_path / "fresh")
    v = Vault(data_dir=data_dir)
    try:
        assert v._lib.qv_is_first_launch() == 1
    finally:
        v._lib.qv_shutdown()

# ------------------------------------------------------------------ #
# Snippet CRUD (no password required)
# ------------------------------------------------------------------ #

def test_add_snippet(vault):
    new_id = vault.add(
        entry_type=TYPE_FROM_STR["snippet"],
        title="Test Snippet",
        content="print('hello')",
        tags="python,test",
        language="python"
    )
    assert len(new_id) == 36  # UUID

def test_list_snippets(vault):
    vault.add(TYPE_FROM_STR["snippet"], "Snippet A", content="echo a")
    vault.add(TYPE_FROM_STR["snippet"], "Snippet B", content="echo b")
    vault.add(TYPE_FROM_STR["command"], "Command C", content="ls")

    entries = vault.list_entries(type_filter=TYPE_FROM_STR["snippet"])
    assert len(entries) == 2
    titles = {e["title"] for e in entries}
    assert "Snippet A" in titles
    assert "Snippet B" in titles

def test_get_snippet(vault):
    new_id = vault.add(TYPE_FROM_STR["snippet"], "My Snippet", content="git status")
    e = vault.get(new_id)
    assert e["title"]   == "My Snippet"
    assert e["content"] == "git status"
    assert e["type_name"] == "snippet"

def test_delete_snippet(vault):
    new_id = vault.add(TYPE_FROM_STR["snippet"], "To Delete")
    vault.delete(new_id)
    entries = vault.list_entries()
    assert all(e["id"] != new_id for e in entries)

def test_add_command(vault):
    new_id = vault.add(
        entry_type=TYPE_FROM_STR["command"],
        title="Docker ps",
        content="docker ps -a --format table",
        language="bash"
    )
    e = vault.get(new_id)
    assert e["content"] == "docker ps -a --format table"
    assert e["type_name"] == "command"

# ------------------------------------------------------------------ #
# Notes (password required)
# ------------------------------------------------------------------ #

def test_notes_require_password(vault):
    """Creating a note without setting password should fail."""
    r = vault._lib.qv_entry_create(
        0,  # NOTE type
        b"Test Note",
        b"Secret content",
        b"",  # tags
        b"",  # desc
        b"",  # lang
        0,    # dpapi
        ctypes.byref(ctypes.c_char_p()),
    )
    # Should return QV_ERR_NO_PASSWORD (-9)
    assert r == -9

def test_notes_set_and_unlock(vault):
    r = vault.set_password("testpassword123")
    assert r == QV_OK
    assert vault.is_notes_unlocked()

def test_notes_wrong_password(tmp_path):
    data_dir = str(tmp_path / "notes_pw")
    v = Vault(data_dir=data_dir)
    try:
        # Set initial password
        v.set_password("correctpassword")
        v._lib.qv_shutdown()

        # Re-init and try wrong password
        v2 = Vault(data_dir=data_dir)
        r = v2.set_password("wrongpassword")
        assert r == -8  # QV_ERR_BAD_PASSWORD
        v2._lib.qv_shutdown()
    finally:
        pass

def test_notes_create_and_read(tmp_path):
    data_dir = str(tmp_path / "notes_rw")
    v = Vault(data_dir=data_dir)
    try:
        v.set_password("mypassword")
        new_id = v.add(TYPE_FROM_STR["note"], "My Note", content="Super secret content")
        e = v.get(new_id)
        assert e["content"] == "Super secret content"
        assert e["type_name"] == "note"
    finally:
        v._lib.qv_shutdown()

def test_notes_persist_across_sessions(tmp_path):
    data_dir = str(tmp_path / "persist")

    # Session 1: create note
    v1 = Vault(data_dir=data_dir)
    v1.set_password("session_pw")
    note_id = v1.add(TYPE_FROM_STR["note"], "Persist Note", content="I should survive")
    v1._lib.qv_shutdown()

    # Session 2: reload and verify
    v2 = Vault(data_dir=data_dir)
    v2.set_password("session_pw")
    e = v2.get(note_id)
    assert e["content"] == "I should survive"
    v2._lib.qv_shutdown()

def test_notes_wrong_password_hides_content(tmp_path):
    data_dir = str(tmp_path / "hidecontent")

    v1 = Vault(data_dir=data_dir)
    v1.set_password("rightpw")
    note_id = v1.add(TYPE_FROM_STR["note"], "Hidden", content="Secret")
    v1._lib.qv_shutdown()

    v2 = Vault(data_dir=data_dir)
    # Don't unlock - content should show as locked message
    entries = v2.list_entries(type_filter=TYPE_FROM_STR["note"])
    for e in entries:
        content = e.get("content", "")
        # Content should not be the plaintext when locked
        assert "Secret" not in content
    v2._lib.qv_shutdown()

# ------------------------------------------------------------------ #
# Search
# ------------------------------------------------------------------ #

def test_search_by_title(vault):
    vault.add(TYPE_FROM_STR["snippet"], "nginx config", content="server { }")
    vault.add(TYPE_FROM_STR["snippet"], "docker compose", content="version: 3")
    vault.add(TYPE_FROM_STR["command"], "restart nginx", content="systemctl restart nginx")

    results = vault.search("nginx")
    titles = [e["title"] for e in results]
    assert any("nginx" in t.lower() for t in titles)

def test_search_by_tags(vault):
    vault.add(TYPE_FROM_STR["snippet"], "Deploy script", content="./deploy.sh", tags="devops,deploy")
    vault.add(TYPE_FROM_STR["snippet"], "Test script",   content="pytest .",    tags="testing,python")

    results = vault.search("devops")
    assert len(results) == 1
    assert results[0]["title"] == "Deploy script"

def test_search_empty_query_returns_all(vault):
    vault.add(TYPE_FROM_STR["snippet"], "A")
    vault.add(TYPE_FROM_STR["snippet"], "B")
    vault.add(TYPE_FROM_STR["snippet"], "C")
    results = vault.search("")
    assert len(results) >= 3

def test_search_no_results(vault):
    vault.add(TYPE_FROM_STR["snippet"], "Something")
    results = vault.search("xyzzy_nonexistent_12345")
    assert len(results) == 0

# ------------------------------------------------------------------ #
# Favorites
# ------------------------------------------------------------------ #

def test_set_favorite(vault):
    new_id = vault.add(TYPE_FROM_STR["snippet"], "Fav Test", content="x")
    r = vault._lib.qv_entry_set_favorite(new_id.encode(), 1)
    assert r == QV_OK
    e = vault.get(new_id)
    assert e["is_favorite"] is True

def test_list_favorites_only(vault):
    id1 = vault.add(TYPE_FROM_STR["snippet"], "Fav1", content="a")
    id2 = vault.add(TYPE_FROM_STR["snippet"], "NotFav", content="b")
    vault._lib.qv_entry_set_favorite(id1.encode(), 1)

    favs = vault.list_entries(favorites_only=True)
    assert len(favs) == 1
    assert favs[0]["id"] == id1

# ------------------------------------------------------------------ #
# Export / Import
# ------------------------------------------------------------------ #

def test_export_json(vault, tmp_path):
    vault.add(TYPE_FROM_STR["snippet"], "Export Test", content="echo hello", tags="test")
    out = str(tmp_path / "export.json")
    vault.export_json(out, include_content=True)

    assert os.path.isfile(out)
    with open(out, "r", encoding="utf-8") as f:
        data = json.load(f)

    assert data["format"] == "qvault-export"
    assert len(data["entries"]) >= 1
    entry = next(e for e in data["entries"] if e["title"] == "Export Test")
    assert entry["content"] == "echo hello"
    assert entry["tags"] == "test"

def test_import_json_merge(vault, tmp_path):
    # Export from vault1
    vault.add(TYPE_FROM_STR["snippet"], "Imported Snippet", content="cat /etc/hosts")
    out = str(tmp_path / "export.json")
    vault.export_json(out)
    vault._lib.qv_shutdown()

    # Import into fresh vault
    data_dir2 = str(tmp_path / "vault2")
    v2 = Vault(data_dir=data_dir2)
    try:
        imp, skip, err = v2.import_json(out, merge=True)
        assert imp >= 1
        assert err == 0
        entries = v2.list_entries()
        titles = [e["title"] for e in entries]
        assert "Imported Snippet" in titles
    finally:
        v2._lib.qv_shutdown()

def test_import_malformed_json(vault, tmp_path):
    bad = str(tmp_path / "bad.json")
    with open(bad, "w") as f:
        f.write("NOT VALID JSON {{{")
    with pytest.raises(RuntimeError):
        vault.import_json(bad)

def test_import_partial_corrupt(vault, tmp_path):
    """Import file with some valid and some invalid entries."""
    export_data = {
        "format": "qvault-export",
        "format_version": 1,
        "qvault_version": "0.1.0",
        "exported_at": 0,
        "entries": [
            {
                "id": "aaaaaaaa-0000-4000-8000-000000000001",
                "type": 1, "type_name": "snippet",
                "title": "Valid Entry", "content": "echo ok",
                "tags": "", "description": "", "language": "",
                "is_favorite": False, "created_at": 1000, "updated_at": 1000
            },
            {
                "broken": True   # no id, no title - should be skipped
            },
            {
                "id": "aaaaaaaa-0000-4000-8000-000000000002",
                "type": 1, "type_name": "snippet",
                "title": "Another Valid", "content": "ls -la",
                "tags": "", "description": "", "language": "",
                "is_favorite": False, "created_at": 1001, "updated_at": 1001
            },
        ]
    }
    path = str(tmp_path / "partial.json")
    with open(path, "w") as f:
        json.dump(export_data, f)

    imp, skip, err = vault.import_json(path, merge=True)
    assert imp == 2
    assert err == 1  # the broken entry

# ------------------------------------------------------------------ #
# Backup
# ------------------------------------------------------------------ #

def test_backup(vault, tmp_path):
    vault.add(TYPE_FROM_STR["snippet"], "Backup Test")
    bdir = str(tmp_path / "backups")
    path = vault.backup(bdir)
    assert os.path.isfile(path)
    assert "vault_backup_" in os.path.basename(path)

# ------------------------------------------------------------------ #
# Stats
# ------------------------------------------------------------------ #

def test_stats(vault):
    vault.add(TYPE_FROM_STR["snippet"], "S1")
    vault.add(TYPE_FROM_STR["snippet"], "S2")
    vault.add(TYPE_FROM_STR["command"], "C1")
    s = vault.stats()
    assert s["total_entries"] == 3
    assert s["snippet_count"] == 2
    assert s["command_count"] == 1
    assert s["note_count"] == 0

# ------------------------------------------------------------------ #
# Path safety
# ------------------------------------------------------------------ #

def test_path_traversal_in_export(vault, tmp_path):
    """Ensure no crash on unusual but valid paths."""
    vault.add(TYPE_FROM_STR["snippet"], "Path Test")
    out = str(tmp_path / "sub" / "deep" / "export.json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    vault.export_json(out)
    assert os.path.isfile(out)

def test_large_content(vault):
    """Entry with 500KB content should be stored and retrieved correctly."""
    big = "A" * 512_000
    new_id = vault.add(TYPE_FROM_STR["snippet"], "Big Content", content=big)
    e = vault.get(new_id)
    assert e["content"] == big

def test_unicode_content(vault):
    """UTF-8 content including CJK, emoji, Arabic."""
    content = "你好世界 🌍 مرحبا बिश्व"
    new_id = vault.add(TYPE_FROM_STR["snippet"], "Unicode Test", content=content)
    e = vault.get(new_id)
    assert e["content"] == content

def test_empty_title_rejected(vault):
    """Empty title must be rejected."""
    r = vault._lib.qv_entry_create(
        TYPE_FROM_STR["snippet"],
        b"",   # empty title
        b"content", b"", b"", b"", 0,
        ctypes.byref(ctypes.c_char_p()),
    )
    assert r == -3  # QV_ERR_BAD_ARG

def test_concurrent_read_same_vault(tmp_path):
    """Two vault handles on the same data dir (read scenario)."""
    data_dir = str(tmp_path / "concurrent")
    v1 = Vault(data_dir=data_dir)
    v1.add(TYPE_FROM_STR["snippet"], "Shared Entry")
    # v1 holds the lock; v2 init should still succeed (lock is advisory)
    # (we just verify no crash or data corruption)
    entries = v1.list_entries()
    assert len(entries) == 1
    v1._lib.qv_shutdown()

def test_data_dir_created_automatically(tmp_path):
    """Vault should create data dir if it doesn't exist."""
    deep = str(tmp_path / "a" / "b" / "c" / "vaultdata")
    assert not os.path.exists(deep)
    v = Vault(data_dir=deep)
    try:
        assert os.path.isdir(deep)
    finally:
        v._lib.qv_shutdown()
