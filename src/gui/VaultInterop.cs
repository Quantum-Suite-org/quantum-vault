using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QuantumVault
{
    // Maps to QVResult enum in C header
    public enum QVResult : int
    {
        OK           =  0,
        NotInit      = -1,
        AlreadyInit  = -2,
        BadArg       = -3,
        NotFound     = -4,
        IO           = -5,
        Corrupt      = -6,
        Crypto       = -7,
        BadPassword  = -8,
        NoPassword   = -9,
        Locked       = -10,
        Duplicate    = -11,
        TooLarge     = -12,
        Internal     = -99,
    }

    public enum QVEntryType : int
    {
        Note    = 0,
        Snippet = 1,
        Command = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QVStats
    {
        public int   TotalEntries;
        public int   NoteCount;
        public int   SnippetCount;
        public int   CommandCount;
        public int   FavoriteCount;
        public long  VaultFileSizeBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QVListResultNative
    {
        public IntPtr Entries; // QVEntry**
        public int    Count;
    }

    /// <summary>
    /// Managed representation of a vault entry.
    /// </summary>
    public class VaultEntry
    {
        public string      Id          { get; set; }
        public QVEntryType Type        { get; set; }
        public string      Title       { get; set; }
        public string      Content     { get; set; }
        public string      Tags        { get; set; }
        public string      Description { get; set; }
        public string      Language    { get; set; }
        public bool        IsFavorite  { get; set; }
        public bool        IsDpapi     { get; set; }
        public DateTime    CreatedAt   { get; set; }
        public DateTime    UpdatedAt   { get; set; }

        public string TypeLabel => Type switch
        {
            QVEntryType.Note    => "Note",
            QVEntryType.Snippet => "Snippet",
            QVEntryType.Command => "Command",
            _                   => "Note"
        };

        public static DateTime FromUnix(long ts)
            => DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
    }

    /// <summary>
    /// P/Invoke bindings for qvault_core.dll.
    /// All native strings are UTF-8; we marshal via IntPtr + Encoding.UTF8.
    /// </summary>
    public static class VaultInterop
    {
        private const string DLL = "qvault_core.dll";

        // ----------------------------------------------------------------
        // Raw P/Invoke declarations
        // ----------------------------------------------------------------

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_version();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_error_string(QVResult code);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_init(IntPtr data_dir, IntPtr log_dir);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void qv_shutdown();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int qv_is_first_launch();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_notes_set_password(IntPtr password);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int qv_notes_is_unlocked();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_notes_change_password(IntPtr old_pw, IntPtr new_pw);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_entry_create(
            QVEntryType type, IntPtr title, IntPtr content,
            IntPtr tags, IntPtr description, IntPtr language,
            int use_dpapi, out IntPtr out_id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_entry_update(
            IntPtr id, IntPtr title, IntPtr content,
            IntPtr tags, IntPtr description, IntPtr language,
            int use_dpapi);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_entry_delete(IntPtr id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_entry_duplicate(IntPtr id, out IntPtr out_id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_entry_set_favorite(IntPtr id, int is_favorite);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_entry_get(IntPtr id, out IntPtr out_entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_entry_clone(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void qv_entry_free(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_entry_id(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVEntryType qv_entry_type(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_entry_title(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_entry_content(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_entry_tags(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_entry_description(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_entry_language(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int qv_entry_is_favorite(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int qv_entry_is_dpapi(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern long qv_entry_created_at(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern long qv_entry_updated_at(IntPtr entry);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_entry_list(
            int type, int favorites_only, int limit, int offset,
            IntPtr sort_by, ref QVListResultNative result);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void qv_list_result_free(ref QVListResultNative result);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_entry_search(
            IntPtr query, int type, int search_content,
            int limit, ref QVListResultNative result);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_entry_recent(int limit, ref QVListResultNative result);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_export_json(IntPtr path, int include_encrypted);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_import_json(
            IntPtr path, int merge,
            out int out_imported, out int out_skipped, out int out_errors);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_backup(IntPtr backup_dir, out IntPtr out_path);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern QVResult qv_get_stats(out QVStats stats);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_get_data_dir();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr qv_get_log_dir();

        // ----------------------------------------------------------------
        // UTF-8 marshalling helpers
        // ----------------------------------------------------------------

        private static string PtrToUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return string.Empty;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return string.Empty;
            var buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        // Allocates a UTF-8 C string on the native heap via Marshal.
        // Caller must call FreeHGlobal on the returned pointer.
        private static IntPtr Utf8Ptr(string s)
        {
            if (s == null) return IntPtr.Zero;
            var bytes = Encoding.UTF8.GetBytes(s);
            var ptr   = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            return ptr;
        }

        private static void Free(IntPtr p) { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }

        // ----------------------------------------------------------------
        // Public managed API
        // ----------------------------------------------------------------

        public static string Version()       => PtrToUtf8(qv_version());
        public static string ErrorString(QVResult r) => PtrToUtf8(qv_error_string(r));

        public static QVResult Init(string dataDir, string logDir)
        {
            var pd = Utf8Ptr(dataDir);
            var pl = Utf8Ptr(logDir);
            try   { return qv_init(pd, pl); }
            finally { Free(pd); Free(pl); }
        }

        public static void Shutdown() => qv_shutdown();
        public static bool IsFirstLaunch() => qv_is_first_launch() != 0;

        public static QVResult NotesSetPassword(string password)
        {
            var p = Utf8Ptr(password);
            try   { return qv_notes_set_password(p); }
            finally { Free(p); }
        }

        public static bool NotesIsUnlocked() => qv_notes_is_unlocked() != 0;

        public static QVResult NotesChangePassword(string oldPw, string newPw)
        {
            var po = Utf8Ptr(oldPw);
            var pn = Utf8Ptr(newPw);
            try   { return qv_notes_change_password(po, pn); }
            finally { Free(po); Free(pn); }
        }

        public static (QVResult result, string id) CreateEntry(
            QVEntryType type, string title, string content,
            string tags, string description, string language, bool useDpapi)
        {
            var ptitle = Utf8Ptr(title);
            var pcont  = Utf8Ptr(content);
            var ptags  = Utf8Ptr(tags);
            var pdesc  = Utf8Ptr(description);
            var plang  = Utf8Ptr(language);
            try
            {
                var r = qv_entry_create(type, ptitle, pcont, ptags, pdesc, plang,
                                        useDpapi ? 1 : 0, out IntPtr outId);
                return (r, r == QVResult.OK ? PtrToUtf8(outId) : null);
            }
            finally { Free(ptitle); Free(pcont); Free(ptags); Free(pdesc); Free(plang); }
        }

        public static QVResult UpdateEntry(string id, string title, string content,
            string tags, string description, string language, int useDpapi = -1)
        {
            var pid    = Utf8Ptr(id);
            var ptitle = Utf8Ptr(title);
            var pcont  = Utf8Ptr(content);
            var ptags  = Utf8Ptr(tags);
            var pdesc  = Utf8Ptr(description);
            var plang  = Utf8Ptr(language);
            try
            {
                return qv_entry_update(pid, ptitle, pcont, ptags, pdesc, plang, useDpapi);
            }
            finally { Free(pid); Free(ptitle); Free(pcont); Free(ptags); Free(pdesc); Free(plang); }
        }

        public static QVResult DeleteEntry(string id)
        {
            var p = Utf8Ptr(id);
            try   { return qv_entry_delete(p); }
            finally { Free(p); }
        }

        public static (QVResult result, string newId) DuplicateEntry(string id)
        {
            var p = Utf8Ptr(id);
            try
            {
                var r = qv_entry_duplicate(p, out IntPtr outId);
                return (r, r == QVResult.OK ? PtrToUtf8(outId) : null);
            }
            finally { Free(p); }
        }

        public static QVResult SetFavorite(string id, bool isFav)
        {
            var p = Utf8Ptr(id);
            try   { return qv_entry_set_favorite(p, isFav ? 1 : 0); }
            finally { Free(p); }
        }

        public static (QVResult result, VaultEntry entry) GetEntry(string id)
        {
            var p = Utf8Ptr(id);
            try
            {
                var r = qv_entry_get(p, out IntPtr ep);
                if (r != QVResult.OK) return (r, null);
                return (r, ReadEntry(ep));
            }
            finally { Free(p); }
        }

        private static VaultEntry ReadEntry(IntPtr ep)
        {
            if (ep == IntPtr.Zero) return null;
            return new VaultEntry
            {
                Id          = PtrToUtf8(qv_entry_id(ep)),
                Type        = qv_entry_type(ep),
                Title       = PtrToUtf8(qv_entry_title(ep)),
                Content     = PtrToUtf8(qv_entry_content(ep)),
                Tags        = PtrToUtf8(qv_entry_tags(ep)),
                Description = PtrToUtf8(qv_entry_description(ep)),
                Language    = PtrToUtf8(qv_entry_language(ep)),
                IsFavorite  = qv_entry_is_favorite(ep) != 0,
                IsDpapi     = qv_entry_is_dpapi(ep) != 0,
                CreatedAt   = VaultEntry.FromUnix(qv_entry_created_at(ep)),
                UpdatedAt   = VaultEntry.FromUnix(qv_entry_updated_at(ep)),
            };
        }

        private static System.Collections.Generic.List<VaultEntry> ReadList(ref QVListResultNative native)
        {
            var list = new System.Collections.Generic.List<VaultEntry>(native.Count);
            for (int i = 0; i < native.Count; i++)
            {
                // entries is QVEntry** → read pointer at index i
                var epPtr = Marshal.ReadIntPtr(native.Entries, i * IntPtr.Size);
                if (epPtr != IntPtr.Zero)
                    list.Add(ReadEntry(epPtr));
            }
            return list;
        }

        public static (QVResult result, System.Collections.Generic.List<VaultEntry> entries)
            ListEntries(int type = -1, bool favoritesOnly = false,
                        int limit = 0, int offset = 0, string sortBy = "updated")
        {
            var psort = Utf8Ptr(sortBy);
            var native = new QVListResultNative();
            try
            {
                var r = qv_entry_list(type, favoritesOnly ? 1 : 0, limit, offset, psort, ref native);
                if (r != QVResult.OK) return (r, new System.Collections.Generic.List<VaultEntry>());
                var list = ReadList(ref native);
                return (r, list);
            }
            finally
            {
                Free(psort);
                qv_list_result_free(ref native);
            }
        }

        public static (QVResult result, System.Collections.Generic.List<VaultEntry> entries)
            SearchEntries(string query, int type = -1, bool searchContent = false, int limit = 100)
        {
            var pq = Utf8Ptr(query);
            var native = new QVListResultNative();
            try
            {
                var r = qv_entry_search(pq, type, searchContent ? 1 : 0, limit, ref native);
                if (r != QVResult.OK) return (r, new System.Collections.Generic.List<VaultEntry>());
                var list = ReadList(ref native);
                return (r, list);
            }
            finally
            {
                Free(pq);
                qv_list_result_free(ref native);
            }
        }

        public static (QVResult result, System.Collections.Generic.List<VaultEntry> entries)
            RecentEntries(int limit = 20)
        {
            var native = new QVListResultNative();
            try
            {
                var r = qv_entry_recent(limit, ref native);
                if (r != QVResult.OK) return (r, new System.Collections.Generic.List<VaultEntry>());
                return (r, ReadList(ref native));
            }
            finally { qv_list_result_free(ref native); }
        }

        public static QVResult ExportJson(string path, bool includeContent)
        {
            var p = Utf8Ptr(path);
            try   { return qv_export_json(p, includeContent ? 1 : 0); }
            finally { Free(p); }
        }

        public static (QVResult result, int imported, int skipped, int errors)
            ImportJson(string path, bool merge)
        {
            var p = Utf8Ptr(path);
            try
            {
                var r = qv_import_json(p, merge ? 1 : 0,
                    out int imp, out int skip, out int err);
                return (r, imp, skip, err);
            }
            finally { Free(p); }
        }

        public static (QVResult result, string backupPath) Backup(string dir = null)
        {
            var pd = Utf8Ptr(dir);
            try
            {
                var r = qv_backup(pd, out IntPtr outp);
                return (r, r == QVResult.OK ? PtrToUtf8(outp) : null);
            }
            finally { Free(pd); }
        }

        public static (QVResult result, QVStats stats) GetStats()
        {
            var r = qv_get_stats(out QVStats s);
            return (r, s);
        }

        public static string GetDataDir() => PtrToUtf8(qv_get_data_dir());
        public static string GetLogDir()  => PtrToUtf8(qv_get_log_dir());
    }
}
