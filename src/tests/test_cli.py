"""
CLI integration tests - tests the CLI layer on top of the vault.
"""
import os
import sys
import json
import shutil
import tempfile
import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src", "cli"))

from qvault.vault import Vault, TYPE_FROM_STR
from qvault import cli as cli_module
import argparse

# ------------------------------------------------------------------ #
# Helpers
# ------------------------------------------------------------------ #

class CapArgs:
    """Minimal args namespace for CLI functions."""
    json     = False
    type     = None
    favorites = False
    limit    = 0
    sort     = "updated"
    content  = False
    data_dir = None
    replace  = False
    yes      = True
    stdin    = False
    password = None
    tags     = None
    description = None
    language = None

@pytest.fixture
def vault_and_dir(tmp_path):
    data_dir = str(tmp_path / "cli_vault")
    v = Vault(data_dir=data_dir)
    yield v, data_dir
    v._lib.qv_shutdown()

# ------------------------------------------------------------------ #
# version
# ------------------------------------------------------------------ #

def test_cli_version(vault_and_dir, capsys):
    v, _ = vault_and_dir
    args = CapArgs()
    cli_module.cmd_version(args, v)
    out = capsys.readouterr().out
    assert "0.1.0" in out

def test_cli_version_json(vault_and_dir, capsys):
    v, _ = vault_and_dir
    args = CapArgs(); args.json = True
    cli_module.cmd_version(args, v)
    data = json.loads(capsys.readouterr().out)
    assert "qvault_version" in data

# ------------------------------------------------------------------ #
# list
# ------------------------------------------------------------------ #

def test_cli_list_empty(vault_and_dir, capsys):
    v, _ = vault_and_dir
    args = CapArgs()
    cli_module.cmd_list(args, v)
    out = capsys.readouterr().out
    assert "no entries" in out

def test_cli_list_with_entries(vault_and_dir, capsys):
    v, _ = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "Snippet One", content="echo 1")
    v.add(TYPE_FROM_STR["command"], "Command One", content="ls")
    args = CapArgs()
    cli_module.cmd_list(args, v)
    out = capsys.readouterr().out
    assert "Snippet One" in out
    assert "Command One" in out

def test_cli_list_type_filter(vault_and_dir, capsys):
    v, _ = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "SnipX", content="a")
    v.add(TYPE_FROM_STR["command"], "CmdX",  content="b")
    args = CapArgs(); args.type = "snippet"
    cli_module.cmd_list(args, v)
    out = capsys.readouterr().out
    assert "SnipX" in out
    assert "CmdX" not in out

def test_cli_list_json(vault_and_dir, capsys):
    v, _ = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "JsonList", content="x")
    args = CapArgs(); args.json = True
    cli_module.cmd_list(args, v)
    data = json.loads(capsys.readouterr().out)
    assert isinstance(data, list)
    assert data[0]["title"] == "JsonList"

# ------------------------------------------------------------------ #
# search
# ------------------------------------------------------------------ #

def test_cli_search_found(vault_and_dir, capsys):
    v, _ = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "nginx reload", content="nginx -s reload", tags="ops")
    args = CapArgs(); args.query = ["nginx"]; args.limit = 20
    cli_module.cmd_search(args, v)
    out = capsys.readouterr().out
    assert "nginx reload" in out

def test_cli_search_not_found(vault_and_dir, capsys):
    v, _ = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "something", content="unrelated")
    args = CapArgs(); args.query = ["xyzzy_nothere"]; args.limit = 20
    cli_module.cmd_search(args, v)
    out = capsys.readouterr().out
    assert "No results" in out

def test_cli_search_json(vault_and_dir, capsys):
    v, _ = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "findme", content="data", tags="tag1")
    args = CapArgs(); args.query = ["findme"]; args.json = True; args.limit = 20
    cli_module.cmd_search(args, v)
    data = json.loads(capsys.readouterr().out)
    assert len(data) >= 1

# ------------------------------------------------------------------ #
# show
# ------------------------------------------------------------------ #

def test_cli_show(vault_and_dir, capsys):
    v, _ = vault_and_dir
    eid = v.add(TYPE_FROM_STR["snippet"], "Show Me", content="cat file.txt")
    args = CapArgs(); args.id = eid
    cli_module.cmd_show(args, v)
    out = capsys.readouterr().out
    assert "Show Me" in out
    assert "cat file.txt" in out

def test_cli_show_partial_id(vault_and_dir, capsys):
    v, _ = vault_and_dir
    eid = v.add(TYPE_FROM_STR["snippet"], "Partial ID Test", content="xyz")
    prefix = eid[:8]
    args = CapArgs(); args.id = prefix
    cli_module.cmd_show(args, v)
    out = capsys.readouterr().out
    assert "Partial ID Test" in out

def test_cli_show_not_found(vault_and_dir):
    v, _ = vault_and_dir
    args = CapArgs(); args.id = "nonexistent-id-prefix-xyz"
    with pytest.raises(SystemExit) as exc:
        cli_module.cmd_show(args, v)
    assert exc.value.code == 1

# ------------------------------------------------------------------ #
# add
# ------------------------------------------------------------------ #

def test_cli_add_snippet(vault_and_dir, capsys):
    v, _ = vault_and_dir
    args = CapArgs()
    args.type        = "snippet"
    args.title       = "CLI Added"
    args.content     = "echo from cli"
    args.tags        = "cli"
    args.language    = "bash"
    args.description = "added via cli"
    cli_module.cmd_add(args, v)
    out = capsys.readouterr().out
    assert "Created snippet" in out

    entries = v.list_entries()
    assert any(e["title"] == "CLI Added" for e in entries)

def test_cli_add_command(vault_and_dir, capsys):
    v, _ = vault_and_dir
    args = CapArgs()
    args.type = "command"; args.title = "My CMD"
    args.content = "Get-Process"; args.tags = "ps"
    args.language = "powershell"; args.description = "list processes"
    cli_module.cmd_add(args, v)
    out = capsys.readouterr().out
    assert "Created command" in out

def test_cli_add_json(vault_and_dir, capsys):
    v, _ = vault_and_dir
    args = CapArgs()
    args.type = "snippet"; args.title = "JSON Add"; args.content = "x"
    args.json = True
    cli_module.cmd_add(args, v)
    data = json.loads(capsys.readouterr().out)
    assert "id" in data
    assert data["title"] == "JSON Add"

# ------------------------------------------------------------------ #
# delete
# ------------------------------------------------------------------ #

def test_cli_delete(vault_and_dir, capsys):
    v, _ = vault_and_dir
    eid = v.add(TYPE_FROM_STR["snippet"], "Delete Me", content="x")
    args = CapArgs(); args.id = eid; args.yes = True
    cli_module.cmd_delete(args, v)
    out = capsys.readouterr().out
    assert "Deleted" in out
    entries = v.list_entries()
    assert all(e["id"] != eid for e in entries)

# ------------------------------------------------------------------ #
# export / import
# ------------------------------------------------------------------ #

def test_cli_export_import_roundtrip(vault_and_dir, tmp_path, capsys):
    v, data_dir = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "Roundtrip Snip", content="round", tags="rt")
    v.add(TYPE_FROM_STR["command"], "Roundtrip Cmd",  content="cmd_rt")

    out_path = str(tmp_path / "rt_export.json")
    args_exp = CapArgs(); args_exp.path = out_path
    cli_module.cmd_export(args_exp, v)
    assert os.path.isfile(out_path)
    capsys.readouterr()

    # Import into new vault
    data_dir2 = str(tmp_path / "vault2")
    v2 = Vault(data_dir=data_dir2)
    try:
        args_imp = CapArgs(); args_imp.path = out_path; args_imp.replace = False
        cli_module.cmd_import(args_imp, v2)
        out = capsys.readouterr().out
        assert "imported" in out.lower()
        entries = v2.list_entries()
        titles = {e["title"] for e in entries}
        assert "Roundtrip Snip" in titles
        assert "Roundtrip Cmd"  in titles
    finally:
        v2._lib.qv_shutdown()

# ------------------------------------------------------------------ #
# stats
# ------------------------------------------------------------------ #

def test_cli_stats(vault_and_dir, capsys):
    v, _ = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "S"); v.add(TYPE_FROM_STR["command"], "C")
    args = CapArgs()
    cli_module.cmd_stats(args, v)
    out = capsys.readouterr().out
    assert "Total entries" in out
    assert "2" in out

def test_cli_stats_json(vault_and_dir, capsys):
    v, _ = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "Sj")
    args = CapArgs(); args.json = True
    cli_module.cmd_stats(args, v)
    data = json.loads(capsys.readouterr().out)
    assert data["total_entries"] == 1
    assert data["snippet_count"] == 1

# ------------------------------------------------------------------ #
# backup
# ------------------------------------------------------------------ #

def test_cli_backup(vault_and_dir, tmp_path, capsys):
    v, _ = vault_and_dir
    v.add(TYPE_FROM_STR["snippet"], "BackupEntry")
    bdir = str(tmp_path / "bkups")
    args = CapArgs(); args.dir = bdir
    cli_module.cmd_backup(args, v)
    out = capsys.readouterr().out
    assert "Backup created" in out
    files = os.listdir(bdir)
    assert any("vault_backup" in f for f in files)
