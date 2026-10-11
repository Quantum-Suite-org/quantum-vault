"""
qvault CLI entry point.
Usage: qvault <command> [options]
"""
import sys
import os
import json
import argparse
import textwrap
from datetime import datetime

from .vault import Vault, TYPE_FROM_STR, TYPE_NAMES, QV_OK

QVAULT_VERSION = "0.1.0"

# ------------------------------------------------------------------ #
# Output helpers
# ------------------------------------------------------------------ #

def _ts(unix):
    try:
        return datetime.fromtimestamp(unix).strftime("%Y-%m-%d %H:%M")
    except Exception:
        return "unknown"

def _fmt_entry_short(e, use_json=False):
    if use_json:
        return e
    type_label = e.get("type_name", "note").upper()[:3]
    fav = "★" if e.get("is_favorite") else " "
    return f"  {fav} [{type_label}] {e['id'][:8]}  {e['title']:<40}  {_ts(e['updated_at'])}"

def _fmt_entry_full(e, use_json=False):
    if use_json:
        return e
    lines = [
        f"ID          : {e['id']}",
        f"Type        : {e.get('type_name','note')}",
        f"Title       : {e['title']}",
        f"Tags        : {e.get('tags','')}",
        f"Description : {e.get('description','')}",
        f"Language    : {e.get('language','')}",
        f"Favorite    : {'yes' if e.get('is_favorite') else 'no'}",
        f"DPAPI       : {'yes' if e.get('is_dpapi') else 'no'}",
        f"Created     : {_ts(e.get('created_at',0))}",
        f"Updated     : {_ts(e.get('updated_at',0))}",
        "",
        "--- Content ---",
        e.get("content", "[locked or empty]"),
    ]
    return "\n".join(lines)

def _print_json(data):
    print(json.dumps(data, ensure_ascii=False, indent=2))

def _die(msg, code=1):
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(code)

# ------------------------------------------------------------------ #
# Command implementations
# ------------------------------------------------------------------ #

def cmd_version(args, vault):
    if args.json:
        _print_json({"qvault_version": QVAULT_VERSION, "core_version": vault.version()})
    else:
        print(f"Quantum Vault {QVAULT_VERSION} (core {vault.version()})")

def cmd_list(args, vault):
    type_filter = -1
    if args.type:
        t = args.type.lower()
        if t not in TYPE_FROM_STR:
            _die(f"Unknown type '{args.type}'. Use: note, snippet, command")
        type_filter = TYPE_FROM_STR[t]

    entries = vault.list_entries(
        type_filter=type_filter,
        favorites_only=args.favorites,
        limit=args.limit,
        sort_by=args.sort or "updated"
    )

    if args.json:
        _print_json(entries)
        return

    if not entries:
        print("  (no entries)")
        return

    header = f"  {'F'} {'TYP'} {'ID':8}  {'TITLE':40}  {'UPDATED'}"
    print(header)
    print("  " + "-" * 72)
    for e in entries:
        print(_fmt_entry_short(e))
    print(f"\n  {len(entries)} entr{'y' if len(entries)==1 else 'ies'}")

def cmd_search(args, vault):
    query = " ".join(args.query)
    if not query:
        _die("Provide a search query.")

    type_filter = -1
    if args.type:
        t = args.type.lower()
        if t not in TYPE_FROM_STR:
            _die(f"Unknown type '{args.type}'")
        type_filter = TYPE_FROM_STR[t]

    entries = vault.search(query, type_filter=type_filter,
                           search_content=args.content, limit=args.limit or 50)

    if args.json:
        _print_json(entries)
        return

    if not entries:
        print(f"  No results for '{query}'")
        return

    print(f"  Results for '{query}':")
    print("  " + "-" * 72)
    for e in entries:
        print(_fmt_entry_short(e))
    print(f"\n  {len(entries)} result(s)")

def cmd_show(args, vault):
    entry_id = args.id
    # Allow partial ID match
    if len(entry_id) < 36:
        all_entries = vault.list_entries()
        matches = [e for e in all_entries if e["id"].startswith(entry_id)]
        if not matches:
            _die(f"No entry found with ID starting with '{entry_id}'")
        if len(matches) > 1:
            _die(f"Ambiguous ID prefix '{entry_id}' matches {len(matches)} entries. Use more characters.")
        entry_id = matches[0]["id"]

    try:
        e = vault.get(entry_id)
    except RuntimeError as ex:
        _die(str(ex))

    if args.json:
        _print_json(e)
        return

    print(_fmt_entry_full(e))

def cmd_add(args, vault):
    type_str = (args.type or "note").lower()
    if type_str not in TYPE_FROM_STR:
        _die(f"Unknown type '{type_str}'. Use: note, snippet, command")
    entry_type = TYPE_FROM_STR[type_str]

    title   = args.title
    content = args.content or ""

    # For notes, require password
    if entry_type == 0:  # note
        if not vault.is_notes_unlocked():
            if args.password:
                r = vault.set_password(args.password)
                if r != QV_OK:
                    _die("Incorrect notes password.")
            else:
                import getpass
                pw = getpass.getpass("Notes password: ")
                r = vault.set_password(pw)
                if r != QV_OK:
                    _die("Incorrect notes password.")

    # Read content from stdin if --stdin flag
    if args.stdin:
        content = sys.stdin.read()

    try:
        new_id = vault.add(
            entry_type=entry_type,
            title=title,
            content=content,
            tags=args.tags or "",
            description=args.description or "",
            language=args.language or "",
        )
    except RuntimeError as ex:
        _die(str(ex))

    if args.json:
        _print_json({"id": new_id, "title": title, "type": type_str})
    else:
        print(f"Created {type_str}: {new_id[:8]}  '{title}'")

def cmd_delete(args, vault):
    entry_id = args.id
    if len(entry_id) < 36:
        all_entries = vault.list_entries()
        matches = [e for e in all_entries if e["id"].startswith(entry_id)]
        if not matches:
            _die(f"No entry found with ID starting with '{entry_id}'")
        if len(matches) > 1:
            _die(f"Ambiguous ID prefix.")
        entry_id = matches[0]["id"]

    if not args.yes:
        try:
            e = vault.get(entry_id)
            confirm = input(f"Delete '{e['title']}' ({entry_id[:8]})? [y/N] ").strip().lower()
        except Exception:
            confirm = input(f"Delete {entry_id[:8]}? [y/N] ").strip().lower()
        if confirm != "y":
            print("Cancelled.")
            return

    try:
        vault.delete(entry_id)
    except RuntimeError as ex:
        _die(str(ex))

    if args.json:
        _print_json({"deleted": entry_id})
    else:
        print(f"Deleted {entry_id[:8]}")

def cmd_copy(args, vault):
    entry_id = args.id
    if len(entry_id) < 36:
        all_entries = vault.list_entries()
        matches = [e for e in all_entries if e["id"].startswith(entry_id)]
        if not matches:
            _die(f"No entry with ID starting with '{entry_id}'")
        if len(matches) > 1:
            _die("Ambiguous ID prefix.")
        entry_id = matches[0]["id"]

    try:
        e = vault.get(entry_id)
    except RuntimeError as ex:
        _die(str(ex))

    content = e.get("content", "")
    if not content:
        _die("Entry has no content to copy.")

    ok = vault.copy_to_clipboard(content)
    if args.json:
        _print_json({"copied": ok, "id": entry_id, "length": len(content)})
    else:
        if ok:
            print(f"Copied content of '{e['title']}' to clipboard ({len(content)} chars).")
        else:
            # Print to stdout as fallback
            print(content)

def cmd_export(args, vault):
    path = args.path
    if not path:
        path = f"qvault_export_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"

    try:
        vault.export_json(os.path.abspath(path), include_content=True)
    except RuntimeError as ex:
        _die(str(ex))

    if args.json:
        _print_json({"exported_to": os.path.abspath(path)})
    else:
        print(f"Exported to: {os.path.abspath(path)}")
        print("Warning: export contains plaintext content. Keep this file secure.")

def cmd_import(args, vault):
    path = args.path
    if not os.path.isfile(path):
        _die(f"File not found: {path}")

    merge = not args.replace

    try:
        imported, skipped, errors = vault.import_json(os.path.abspath(path), merge=merge)
    except RuntimeError as ex:
        _die(str(ex))

    if args.json:
        _print_json({"imported": imported, "skipped": skipped, "errors": errors})
    else:
        print(f"Import complete: {imported} imported, {skipped} skipped, {errors} errors")

def cmd_backup(args, vault):
    try:
        path = vault.backup(args.dir or None)
    except RuntimeError as ex:
        _die(str(ex))

    if args.json:
        _print_json({"backup_path": path})
    else:
        print(f"Backup created: {path}")

def cmd_stats(args, vault):
    try:
        s = vault.stats()
    except RuntimeError as ex:
        _die(str(ex))

    if args.json:
        _print_json(s)
    else:
        mb = s["vault_file_size_bytes"] / 1048576
        print(f"Total entries : {s['total_entries']}")
        print(f"  Notes       : {s['note_count']}")
        print(f"  Snippets    : {s['snippet_count']}")
        print(f"  Commands    : {s['command_count']}")
        print(f"  Favorites   : {s['favorite_count']}")
        print(f"Data size     : {mb:.2f} MB")
        print(f"Data dir      : {vault.data_dir()}")

# ------------------------------------------------------------------ #
# Argument parser
# ------------------------------------------------------------------ #

def build_parser():
    parser = argparse.ArgumentParser(
        prog="qvault",
        description="Quantum Vault CLI - manage your local vault from the command line.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=textwrap.dedent("""
        Examples:
          qvault list
          qvault list --type snippet
          qvault search "docker run"
          qvault show abc123
          qvault add note "Meeting notes" --stdin
          qvault add snippet "Git stash pop" --content "git stash pop" --language bash
          qvault copy abc123
          qvault export --path backup.json
          qvault import backup.json
          qvault version
          qvault stats
        """)
    )

    parser.add_argument("--json",     action="store_true", help="Output as JSON")
    parser.add_argument("--data-dir", metavar="DIR", help="Override vault data directory")
    parser.add_argument("--version",  action="store_true", help="Print version and exit")

    sub = parser.add_subparsers(dest="command")

    # version
    p_ver = sub.add_parser("version", help="Print version information")
    p_ver.add_argument("--json", action="store_true")

    # list
    p_list = sub.add_parser("list", help="List vault entries")
    p_list.add_argument("--type",      metavar="TYPE", help="Filter by type: note, snippet, command")
    p_list.add_argument("--favorites", action="store_true", help="Show only favorites")
    p_list.add_argument("--limit",     type=int, default=0, help="Max results (0=all)")
    p_list.add_argument("--sort",      metavar="FIELD", default="updated",
                        help="Sort by: updated, created, title")
    p_list.add_argument("--json",      action="store_true")

    # search
    p_search = sub.add_parser("search", help="Search entries")
    p_search.add_argument("query",   nargs="+", help="Search query")
    p_search.add_argument("--type",  metavar="TYPE")
    p_search.add_argument("--content", action="store_true", help="Also search content")
    p_search.add_argument("--limit", type=int, default=50)
    p_search.add_argument("--json",  action="store_true")

    # show
    p_show = sub.add_parser("show", help="Show entry details and content")
    p_show.add_argument("id", help="Entry ID or ID prefix")
    p_show.add_argument("--json", action="store_true")

    # add
    p_add = sub.add_parser("add", help="Add a new entry")
    p_add.add_argument("type",    help="Entry type: note, snippet, command")
    p_add.add_argument("title",   help="Entry title")
    p_add.add_argument("--content",     metavar="TEXT", help="Entry content")
    p_add.add_argument("--stdin",       action="store_true", help="Read content from stdin")
    p_add.add_argument("--tags",        metavar="TAGS", help="Comma-separated tags")
    p_add.add_argument("--description", metavar="DESC")
    p_add.add_argument("--language",    metavar="LANG")
    p_add.add_argument("--password",    metavar="PW", help="Notes master password")
    p_add.add_argument("--json",        action="store_true")

    # delete
    p_del = sub.add_parser("delete", help="Delete an entry")
    p_del.add_argument("id",            help="Entry ID or prefix")
    p_del.add_argument("--yes", "-y",   action="store_true", help="Skip confirmation")
    p_del.add_argument("--json",        action="store_true")

    # copy
    p_copy = sub.add_parser("copy", help="Copy entry content to clipboard")
    p_copy.add_argument("id",    help="Entry ID or prefix")
    p_copy.add_argument("--json", action="store_true")

    # export
    p_exp = sub.add_parser("export", help="Export vault to JSON")
    p_exp.add_argument("--path", metavar="FILE", help="Output file path")
    p_exp.add_argument("--json", action="store_true")

    # import
    p_imp = sub.add_parser("import", help="Import vault from JSON export")
    p_imp.add_argument("path",    help="Import file path")
    p_imp.add_argument("--replace", action="store_true", help="Replace all data (default: merge)")
    p_imp.add_argument("--json",    action="store_true")

    # backup
    p_bak = sub.add_parser("backup", help="Create a backup of vault data")
    p_bak.add_argument("--dir",  metavar="DIR", help="Backup directory")
    p_bak.add_argument("--json", action="store_true")

    # stats
    p_stat = sub.add_parser("stats", help="Show vault statistics")
    p_stat.add_argument("--json", action="store_true")

    return parser

# ------------------------------------------------------------------ #
# Main
# ------------------------------------------------------------------ #

def main():
    parser = build_parser()
    args   = parser.parse_args()

    # --version flag at top level
    if getattr(args, "version", False) and not args.command:
        print(f"Quantum Vault {QVAULT_VERSION}")
        sys.exit(0)

    if not args.command:
        parser.print_help()
        sys.exit(0)

    # JSON flag propagation from top-level
    if hasattr(args, "json") and not args.json:
        # If top-level --json was set before subcommand
        pass

    try:
        vault = Vault(data_dir=getattr(args, "data_dir", None))
    except FileNotFoundError as ex:
        _die(str(ex))
    except RuntimeError as ex:
        _die(str(ex))

    use_json = getattr(args, "json", False)

    dispatch = {
        "version": cmd_version,
        "list":    cmd_list,
        "search":  cmd_search,
        "show":    cmd_show,
        "add":     cmd_add,
        "delete":  cmd_delete,
        "copy":    cmd_copy,
        "export":  cmd_export,
        "import":  cmd_import,
        "backup":  cmd_backup,
        "stats":   cmd_stats,
    }

    fn = dispatch.get(args.command)
    if fn is None:
        _die(f"Unknown command '{args.command}'")

    try:
        fn(args, vault)
    except KeyboardInterrupt:
        print("\nInterrupted.", file=sys.stderr)
        sys.exit(130)
    except Exception as ex:
        if use_json:
            _print_json({"error": str(ex)})
        else:
            _die(str(ex))
    finally:
        vault._lib.qv_shutdown()


if __name__ == "__main__":
    main()
