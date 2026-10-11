/*
 * qvault_core.cpp - Quantum Vault Core Library Implementation
 * Version: 0.1.0
 *
 * Implements: AES-256-CBC encryption, PBKDF2-SHA256 key derivation,
 * DPAPI wrapping, JSON persistence, full-text search, file locking.
 *
 * Dependencies: Windows CNG (bcrypt.dll), Windows DPAPI (crypt32.dll),
 * Windows file APIs only. No third-party libraries.
 */

/* QVAULT_CORE_EXPORTS defined on command line for DLL builds */
#include "qvault_core.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <dpapi.h>
#include <wincrypt.h>
#include <shlobj.h>

#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "crypt32.lib")

#include <string>
#include <vector>
#include <unordered_map>
#include <algorithm>
#include <sstream>
#include <fstream>
#include <mutex>
#include <chrono>
#include <cstring>
#include <cassert>
#include <cctype>
#include <stdexcept>
#include <memory>
#include <random>
#include <functional>

/* ------------------------------------------------------------------ */
/* Logging (no note content ever logged)                                */
/* ------------------------------------------------------------------ */

static std::string g_log_dir;
static FILE* g_log_file = nullptr;
static std::mutex g_log_mutex;

static void log_write(const char* level, const char* msg) {
    if (!g_log_file) return;
    std::lock_guard<std::mutex> lk(g_log_mutex);
    SYSTEMTIME st;
    GetLocalTime(&st);
    fprintf(g_log_file,
        "[%04d-%02d-%02d %02d:%02d:%02d] [%s] %s\n",
        st.wYear, st.wMonth, st.wDay,
        st.wHour, st.wMinute, st.wSecond,
        level, msg);
    fflush(g_log_file);
}

#define LOG_INFO(msg)  log_write("INFO",  msg)
#define LOG_WARN(msg)  log_write("WARN",  msg)
#define LOG_ERR(msg)   log_write("ERROR", msg)

/* ------------------------------------------------------------------ */
/* Simple JSON builder / parser (no third-party library)               */
/* ------------------------------------------------------------------ */

/* Escape a UTF-8 string for JSON */
static std::string json_escape(const std::string& s) {
    std::string out;
    out.reserve(s.size() + 8);
    for (unsigned char c : s) {
        switch (c) {
            case '"':  out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\b': out += "\\b";  break;
            case '\f': out += "\\f";  break;
            case '\n': out += "\\n";  break;
            case '\r': out += "\\r";  break;
            case '\t': out += "\\t";  break;
            default:
                if (c < 0x20) {
                    char buf[8];
                    snprintf(buf, sizeof(buf), "\\u%04x", c);
                    out += buf;
                } else {
                    out += (char)c;
                }
        }
    }
    return out;
}

/* Minimal JSON string unescaping */
static std::string json_unescape(const std::string& s) {
    std::string out;
    out.reserve(s.size());
    for (size_t i = 0; i < s.size(); ++i) {
        if (s[i] == '\\' && i + 1 < s.size()) {
            ++i;
            switch (s[i]) {
                case '"':  out += '"';  break;
                case '\\': out += '\\'; break;
                case '/':  out += '/';  break;
                case 'b':  out += '\b'; break;
                case 'f':  out += '\f'; break;
                case 'n':  out += '\n'; break;
                case 'r':  out += '\r'; break;
                case 't':  out += '\t'; break;
                case 'u': {
                    if (i + 4 < s.size()) {
                        std::string hex = s.substr(i+1, 4);
                        unsigned int cp = std::stoul(hex, nullptr, 16);
                        i += 4;
                        if (cp < 0x80) {
                            out += (char)cp;
                        } else if (cp < 0x800) {
                            out += (char)(0xC0 | (cp >> 6));
                            out += (char)(0x80 | (cp & 0x3F));
                        } else {
                            out += (char)(0xE0 | (cp >> 12));
                            out += (char)(0x80 | ((cp >> 6) & 0x3F));
                            out += (char)(0x80 | (cp & 0x3F));
                        }
                    }
                    break;
                }
                default: out += s[i]; break;
            }
        } else {
            out += s[i];
        }
    }
    return out;
}

/* Simple recursive-descent JSON parser producing a string map */
struct JsonValue {
    enum Kind { Null, Bool, Number, String, Array, Object } kind = Null;
    bool   b  = false;
    double n  = 0.0;
    std::string s;
    std::vector<JsonValue> arr;
    std::vector<std::pair<std::string, JsonValue>> obj;

    std::string str() const { return s; }
    int64_t     i64() const { return (int64_t)n; }
    bool        boolean() const { return b; }

    const JsonValue* get(const std::string& key) const {
        for (auto& p : obj)
            if (p.first == key) return &p.second;
        return nullptr;
    }
    const JsonValue* get(size_t idx) const {
        if (idx < arr.size()) return &arr[idx];
        return nullptr;
    }
};

struct JsonParser {
    const char* p;
    const char* end;

    void skip_ws() {
        while (p < end && (*p == ' ' || *p == '\t' || *p == '\n' || *p == '\r'))
            ++p;
    }

    std::string parse_string_raw() {
        if (p >= end || *p != '"') throw std::runtime_error("expected '\"'");
        ++p;
        std::string out;
        while (p < end && *p != '"') {
            if (*p == '\\') {
                ++p;
                if (p >= end) throw std::runtime_error("bad escape");
                out += '\\';
                out += *p++;
            } else {
                out += *p++;
            }
        }
        if (p >= end) throw std::runtime_error("unterminated string");
        ++p; /* consume closing " */
        return out;
    }

    JsonValue parse_value() {
        skip_ws();
        if (p >= end) throw std::runtime_error("unexpected end");
        JsonValue v;
        if (*p == '"') {
            v.kind = JsonValue::String;
            v.s = json_unescape(parse_string_raw());
        } else if (*p == '{') {
            v.kind = JsonValue::Object;
            ++p;
            skip_ws();
            if (p < end && *p == '}') { ++p; return v; }
            while (p < end) {
                skip_ws();
                std::string key = json_unescape(parse_string_raw());
                skip_ws();
                if (p >= end || *p != ':') throw std::runtime_error("expected ':'");
                ++p;
                v.obj.push_back({key, parse_value()});
                skip_ws();
                if (p >= end) break;
                if (*p == '}') { ++p; break; }
                if (*p == ',') { ++p; continue; }
                throw std::runtime_error("expected ',' or '}'");
            }
        } else if (*p == '[') {
            v.kind = JsonValue::Array;
            ++p;
            skip_ws();
            if (p < end && *p == ']') { ++p; return v; }
            while (p < end) {
                v.arr.push_back(parse_value());
                skip_ws();
                if (p >= end) break;
                if (*p == ']') { ++p; break; }
                if (*p == ',') { ++p; continue; }
                throw std::runtime_error("expected ',' or ']'");
            }
        } else if (*p == 't') {
            if (end - p >= 4 && memcmp(p, "true", 4) == 0) {
                v.kind = JsonValue::Bool; v.b = true; p += 4;
            } else throw std::runtime_error("bad token");
        } else if (*p == 'f') {
            if (end - p >= 5 && memcmp(p, "false", 5) == 0) {
                v.kind = JsonValue::Bool; v.b = false; p += 5;
            } else throw std::runtime_error("bad token");
        } else if (*p == 'n') {
            if (end - p >= 4 && memcmp(p, "null", 4) == 0) {
                v.kind = JsonValue::Null; p += 4;
            } else throw std::runtime_error("bad token");
        } else if (*p == '-' || isdigit((unsigned char)*p)) {
            char* ep = nullptr;
            v.n = strtod(p, &ep);
            v.kind = JsonValue::Number;
            p = ep;
        } else {
            throw std::runtime_error(std::string("unexpected char: ") + *p);
        }
        return v;
    }

    JsonValue parse(const std::string& src) {
        p = src.c_str();
        end = p + src.size();
        return parse_value();
    }
};

/* ------------------------------------------------------------------ */
/* Crypto: AES-256-CBC via Windows CNG                                 */
/* ------------------------------------------------------------------ */

static const size_t AES_KEY_BYTES   = 32; /* 256 bits */
static const size_t AES_BLOCK_BYTES = 16;
static const size_t AES_IV_BYTES    = 16;
static const size_t PBKDF2_ITER     = 200000;
static const size_t SALT_BYTES      = 32;
static const size_t HMAC_BYTES      = 32;

/* Base64 encode/decode (no line breaks) */
static const char B64_CHARS[] =
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

static std::string base64_encode(const void* data, size_t len) {
    const uint8_t* in = (const uint8_t*)data;
    std::string out;
    out.reserve(((len + 2) / 3) * 4);
    for (size_t i = 0; i < len; i += 3) {
        uint32_t b = (uint32_t)in[i] << 16;
        if (i+1 < len) b |= (uint32_t)in[i+1] << 8;
        if (i+2 < len) b |= (uint32_t)in[i+2];
        out += B64_CHARS[(b >> 18) & 0x3F];
        out += B64_CHARS[(b >> 12) & 0x3F];
        out += (i+1 < len) ? B64_CHARS[(b >> 6) & 0x3F] : '=';
        out += (i+2 < len) ? B64_CHARS[(b >> 0) & 0x3F] : '=';
    }
    return out;
}

static std::vector<uint8_t> base64_decode(const std::string& s) {
    static const int8_t T[256] = {
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,62,-1,-1,-1,63,
        52,53,54,55,56,57,58,59,60,61,-1,-1,-1, 0,-1,-1,
        -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,
        15,16,17,18,19,20,21,22,23,24,25,-1,-1,-1,-1,-1,
        -1,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,
        41,42,43,44,45,46,47,48,49,50,51,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
    };
    std::vector<uint8_t> out;
    out.reserve((s.size() / 4) * 3);
    int buf = 0, bits = 0;
    for (unsigned char c : s) {
        if (c == '=') break;
        int v = T[c];
        if (v < 0) continue;
        buf = (buf << 6) | v;
        bits += 6;
        if (bits >= 8) {
            bits -= 8;
            out.push_back((uint8_t)(buf >> bits));
            buf &= (1 << bits) - 1;
        }
    }
    return out;
}

/* Secure memory wipe */
static void secure_zero(void* p, size_t n) {
    SecureZeroMemory(p, n);
}

/* Generate random bytes via CNG */
static bool cng_random(void* buf, size_t len) {
    BCRYPT_ALG_HANDLE h = nullptr;
    if (BCryptOpenAlgorithmProvider(&h, BCRYPT_RNG_ALGORITHM, nullptr, 0) != 0)
        return false;
    bool ok = BCryptGenRandom(h, (PUCHAR)buf, (ULONG)len, 0) == 0;
    BCryptCloseAlgorithmProvider(h, 0);
    return ok;
}

/* PBKDF2-SHA256 via BCryptDeriveKeyPBKDF2 (Windows 8+, supported on Win10+) */
static bool pbkdf2_sha256(
    const uint8_t* password, size_t pass_len,
    const uint8_t* salt,     size_t salt_len,
    uint32_t       iterations,
    uint8_t*       out,      size_t out_len)
{
    BCRYPT_ALG_HANDLE hAlg = nullptr;
    bool ok = false;

    /* Open PBKDF2 provider with HMAC-SHA256 as the PRF */
    if (BCryptOpenAlgorithmProvider(&hAlg, BCRYPT_SHA256_ALGORITHM,
                                    nullptr, BCRYPT_ALG_HANDLE_HMAC_FLAG) != 0)
        return false;

    NTSTATUS st = BCryptDeriveKeyPBKDF2(
        hAlg,
        (PUCHAR)password, (ULONG)pass_len,
        (PUCHAR)salt,     (ULONG)salt_len,
        (ULONGLONG)iterations,
        out, (ULONG)out_len,
        0);
    ok = (st == 0);

    BCryptCloseAlgorithmProvider(hAlg, 0);
    return ok;
}

/* AES-256-CBC encrypt. Output: IV(16) || ciphertext */
static bool aes256_encrypt(
    const uint8_t* key,       /* 32 bytes */
    const uint8_t* plaintext, size_t pt_len,
    std::vector<uint8_t>& out)
{
    BCRYPT_ALG_HANDLE hAlg = nullptr;
    BCRYPT_KEY_HANDLE hKey = nullptr;
    bool ok = false;

    if (BCryptOpenAlgorithmProvider(&hAlg, BCRYPT_AES_ALGORITHM, nullptr, 0) != 0)
        return false;
    if (BCryptSetProperty(hAlg, BCRYPT_CHAINING_MODE,
                          (PUCHAR)BCRYPT_CHAIN_MODE_CBC,
                          sizeof(BCRYPT_CHAIN_MODE_CBC), 0) != 0)
        goto done;

    if (BCryptGenerateSymmetricKey(hAlg, &hKey, nullptr, 0,
                                   (PUCHAR)key, AES_KEY_BYTES, 0) != 0)
        goto done;

    {
        uint8_t iv[AES_IV_BYTES];
        if (!cng_random(iv, sizeof(iv))) goto done;

        /* PKCS7 padding */
        size_t pad_len = AES_BLOCK_BYTES - (pt_len % AES_BLOCK_BYTES);
        std::vector<uint8_t> padded(pt_len + pad_len);
        if (pt_len) memcpy(padded.data(), plaintext, pt_len);
        for (size_t i = pt_len; i < padded.size(); ++i)
            padded[i] = (uint8_t)pad_len;

        ULONG enc_len = 0;
        std::vector<uint8_t> ciphertext(padded.size());
        uint8_t iv_copy[AES_IV_BYTES];
        memcpy(iv_copy, iv, sizeof(iv));

        if (BCryptEncrypt(hKey, padded.data(), (ULONG)padded.size(),
                          nullptr, iv_copy, AES_IV_BYTES,
                          ciphertext.data(), (ULONG)ciphertext.size(),
                          &enc_len, 0) != 0)
            goto done_key;

        out.resize(AES_IV_BYTES + enc_len);
        memcpy(out.data(), iv, AES_IV_BYTES);
        memcpy(out.data() + AES_IV_BYTES, ciphertext.data(), enc_len);
        secure_zero(padded.data(), padded.size());
        ok = true;
    }

done_key:
    BCryptDestroyKey(hKey);
done:
    BCryptCloseAlgorithmProvider(hAlg, 0);
    return ok;
}

/* AES-256-CBC decrypt. Input: IV(16) || ciphertext */
static bool aes256_decrypt(
    const uint8_t*              key,    /* 32 bytes */
    const std::vector<uint8_t>& enc,
    std::string&                out)
{
    if (enc.size() < AES_IV_BYTES + AES_BLOCK_BYTES) return false;

    BCRYPT_ALG_HANDLE hAlg = nullptr;
    BCRYPT_KEY_HANDLE hKey = nullptr;
    bool ok = false;

    if (BCryptOpenAlgorithmProvider(&hAlg, BCRYPT_AES_ALGORITHM, nullptr, 0) != 0)
        return false;
    if (BCryptSetProperty(hAlg, BCRYPT_CHAINING_MODE,
                          (PUCHAR)BCRYPT_CHAIN_MODE_CBC,
                          sizeof(BCRYPT_CHAIN_MODE_CBC), 0) != 0)
        goto done;

    if (BCryptGenerateSymmetricKey(hAlg, &hKey, nullptr, 0,
                                   (PUCHAR)key, AES_KEY_BYTES, 0) != 0)
        goto done;

    {
        uint8_t iv[AES_IV_BYTES];
        memcpy(iv, enc.data(), AES_IV_BYTES);
        size_t ct_len = enc.size() - AES_IV_BYTES;

        std::vector<uint8_t> plain(ct_len);
        ULONG dec_len = 0;
        if (BCryptDecrypt(hKey, (PUCHAR)(enc.data() + AES_IV_BYTES), (ULONG)ct_len,
                          nullptr, iv, AES_IV_BYTES,
                          plain.data(), (ULONG)plain.size(), &dec_len, 0) != 0)
            goto done_key;

        /* Remove PKCS7 padding */
        if (dec_len == 0) goto done_key;
        uint8_t pad = plain[dec_len - 1];
        if (pad == 0 || pad > AES_BLOCK_BYTES) goto done_key;
        for (ULONG i = dec_len - pad; i < dec_len; ++i)
            if (plain[i] != pad) goto done_key;

        out.assign((char*)plain.data(), dec_len - pad);
        secure_zero(plain.data(), plain.size());
        ok = true;

done_key:
        BCryptDestroyKey(hKey);
    }
done:
    BCryptCloseAlgorithmProvider(hAlg, 0);
    return ok;
}

/* DPAPI wrap/unwrap */
static bool dpapi_protect(const uint8_t* in, size_t in_len,
                           std::vector<uint8_t>& out) {
    DATA_BLOB src, dst;
    src.pbData = (BYTE*)in;
    src.cbData = (DWORD)in_len;
    if (!CryptProtectData(&src, L"QVaultNote", nullptr, nullptr, nullptr,
                          CRYPTPROTECT_LOCAL_MACHINE & 0 /* user scope */,
                          &dst))
        return false;
    out.assign(dst.pbData, dst.pbData + dst.cbData);
    LocalFree(dst.pbData);
    return true;
}

static bool dpapi_unprotect(const uint8_t* in, size_t in_len,
                             std::vector<uint8_t>& out) {
    DATA_BLOB src, dst;
    src.pbData = (BYTE*)in;
    src.cbData = (DWORD)in_len;
    if (!CryptUnprotectData(&src, nullptr, nullptr, nullptr, nullptr, 0, &dst))
        return false;
    out.assign(dst.pbData, dst.pbData + dst.cbData);
    LocalFree(dst.pbData);
    return true;
}

/* ------------------------------------------------------------------ */
/* UUID generation (random, not network-based)                         */
/* ------------------------------------------------------------------ */

static std::string gen_uuid() {
    uint8_t b[16];
    cng_random(b, 16);
    b[6] = (b[6] & 0x0F) | 0x40; /* version 4 */
    b[8] = (b[8] & 0x3F) | 0x80; /* variant 1 */
    char buf[37];
    snprintf(buf, sizeof(buf),
        "%02x%02x%02x%02x-%02x%02x-%02x%02x-%02x%02x-%02x%02x%02x%02x%02x%02x",
        b[0],b[1],b[2],b[3], b[4],b[5], b[6],b[7],
        b[8],b[9], b[10],b[11],b[12],b[13],b[14],b[15]);
    return std::string(buf);
}

/* ------------------------------------------------------------------ */
/* Timestamp                                                            */
/* ------------------------------------------------------------------ */

static int64_t now_unix() {
    using namespace std::chrono;
    return duration_cast<seconds>(system_clock::now().time_since_epoch()).count();
}

/* ------------------------------------------------------------------ */
/* Entry data structures                                                */
/* ------------------------------------------------------------------ */

struct EntryData {
    std::string id;
    QVEntryType type;
    std::string title;
    std::string content_encrypted_b64; /* base64(IV||ciphertext) */
    std::string tags;
    std::string description;
    std::string language;
    bool        is_favorite = false;
    bool        is_dpapi    = false;
    int64_t     created_at  = 0;
    int64_t     updated_at  = 0;
};

/* Public QVEntry opaque struct */
struct QVEntry {
    EntryData   data;
    std::string content_decrypted; /* populated on demand */
};

/* ------------------------------------------------------------------ */
/* Vault state (global singleton, protected by g_vault_mutex)          */
/* ------------------------------------------------------------------ */

static std::mutex g_vault_mutex;
static bool g_initialized = false;

static std::string g_data_dir;
static std::string g_vault_file;
static std::string g_key_file;
static std::string g_meta_file;

/* Key material */
static uint8_t g_snippet_key[AES_KEY_BYTES] = {};  /* random, stored in key_file */
static bool    g_snippet_key_loaded = false;

static uint8_t g_notes_key[AES_KEY_BYTES] = {};    /* PBKDF2 from password */
static bool    g_notes_unlocked = false;

/* Password verification: store PBKDF2(password, salt2) so we can check */
static uint8_t g_notes_salt[SALT_BYTES]    = {};
static uint8_t g_notes_verify[HMAC_BYTES]  = {};
static bool    g_notes_has_password = false;

/* In-memory store */
static std::unordered_map<std::string, EntryData> g_entries;

/* File lock handle */
static HANDLE g_lock_handle = INVALID_HANDLE_VALUE;

/* ------------------------------------------------------------------ */
/* Path helpers                                                         */
/* ------------------------------------------------------------------ */

static std::string get_appdata_path() {
    WCHAR path[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, 0, path))) {
        int len = WideCharToMultiByte(CP_UTF8, 0, path, -1, nullptr, 0, nullptr, nullptr);
        std::string out(len - 1, '\0');
        WideCharToMultiByte(CP_UTF8, 0, path, -1, &out[0], len, nullptr, nullptr);
        return out;
    }
    return "C:\\Users\\Default\\AppData\\Roaming";
}

static std::string path_join(const std::string& a, const std::string& b) {
    if (a.empty()) return b;
    if (a.back() == '\\' || a.back() == '/') return a + b;
    return a + "\\" + b;
}

static bool dir_exists(const std::string& p) {
    DWORD attr = GetFileAttributesA(p.c_str());
    return attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY);
}

static bool ensure_dir(const std::string& p) {
    if (dir_exists(p)) return true;
    /* Recursively create parent directories */
    size_t pos = p.find_last_of("\\/");
    if (pos != std::string::npos && pos > 0) {
        std::string parent = p.substr(0, pos);
        if (!dir_exists(parent)) {
            if (!ensure_dir(parent)) return false;
        }
    }
    return CreateDirectoryA(p.c_str(), nullptr) != 0 ||
           GetLastError() == ERROR_ALREADY_EXISTS;
}

static bool file_exists(const std::string& p) {
    DWORD attr = GetFileAttributesA(p.c_str());
    return attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY);
}

/* ------------------------------------------------------------------ */
/* File I/O helpers                                                     */
/* ------------------------------------------------------------------ */

static bool read_file_bytes(const std::string& path, std::string& out) {
    std::ifstream f(path, std::ios::binary);
    if (!f) return false;
    out.assign(std::istreambuf_iterator<char>(f), {});
    return true;
}

static bool write_file_bytes(const std::string& path, const std::string& data) {
    std::string tmp = path + ".tmp";
    {
        std::ofstream f(tmp, std::ios::binary | std::ios::trunc);
        if (!f) return false;
        f.write(data.c_str(), (std::streamsize)data.size());
        if (!f) return false;
    }
    /* Atomic replace */
    if (!MoveFileExA(tmp.c_str(), path.c_str(),
                     MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        DeleteFileA(tmp.c_str());
        return false;
    }
    return true;
}

/* ------------------------------------------------------------------ */
/* Snippet/Command key management                                       */
/* ------------------------------------------------------------------ */

static bool load_or_create_snippet_key() {
    if (g_snippet_key_loaded) return true;

    if (file_exists(g_key_file)) {
        std::string raw;
        if (!read_file_bytes(g_key_file, raw)) return false;
        if (raw.size() != AES_KEY_BYTES) return false;
        memcpy(g_snippet_key, raw.data(), AES_KEY_BYTES);
        g_snippet_key_loaded = true;
        return true;
    }

    /* Generate new random key */
    if (!cng_random(g_snippet_key, AES_KEY_BYTES)) return false;

    std::string raw(AES_KEY_BYTES, '\0');
    memcpy(&raw[0], g_snippet_key, AES_KEY_BYTES);
    if (!write_file_bytes(g_key_file, raw)) return false;

    secure_zero(&raw[0], raw.size());
    g_snippet_key_loaded = true;
    LOG_INFO("Generated new snippet/command AES key");
    return true;
}

/* ------------------------------------------------------------------ */
/* Notes encryption helpers                                             */
/* ------------------------------------------------------------------ */

static std::string encrypt_content(const std::string& plaintext,
                                   QVEntryType type,
                                   bool use_dpapi) {
    const uint8_t* key = (type == QV_TYPE_NOTE) ? g_notes_key : g_snippet_key;

    std::vector<uint8_t> enc;
    if (!aes256_encrypt(key, (const uint8_t*)plaintext.c_str(),
                        plaintext.size(), enc))
        return "";

    if (use_dpapi && type == QV_TYPE_NOTE) {
        /* Wrap the ciphertext with DPAPI */
        std::vector<uint8_t> wrapped;
        if (!dpapi_protect(enc.data(), enc.size(), wrapped)) return "";
        return "dpapi:" + base64_encode(wrapped.data(), wrapped.size());
    }

    return base64_encode(enc.data(), enc.size());
}

static bool decrypt_content(const std::string& b64,
                             QVEntryType type, bool is_dpapi,
                             std::string& out) {
    const uint8_t* key = (type == QV_TYPE_NOTE) ? g_notes_key : g_snippet_key;

    std::string actual_b64 = b64;
    bool wrapped = false;

    if (b64.substr(0, 6) == "dpapi:") {
        actual_b64 = b64.substr(6);
        wrapped = true;
    }

    auto enc = base64_decode(actual_b64);
    if (enc.empty()) return false;

    if (wrapped) {
        std::vector<uint8_t> unwrapped;
        if (!dpapi_unprotect(enc.data(), enc.size(), unwrapped)) return false;
        return aes256_decrypt(key, unwrapped, out);
    }

    return aes256_decrypt(key, enc, out);
}

/* ------------------------------------------------------------------ */
/* Persistence: vault.json format                                       */
/* ------------------------------------------------------------------ */

static std::string serialize_vault() {
    std::ostringstream ss;
    ss << "{\n";
    ss << "  \"version\": 1,\n";
    ss << "  \"qvault_version\": \"" << QVAULT_VERSION_STR << "\",\n";
    ss << "  \"notes_has_password\": " << (g_notes_has_password ? "true" : "false") << ",\n";

    /* Store notes salt + verify token as base64 */
    ss << "  \"notes_salt\": \""
       << base64_encode(g_notes_salt, SALT_BYTES) << "\",\n";
    ss << "  \"notes_verify\": \""
       << base64_encode(g_notes_verify, HMAC_BYTES) << "\",\n";

    ss << "  \"entries\": [\n";
    bool first = true;
    for (auto& kv : g_entries) {
        const auto& e = kv.second;
        if (!first) ss << ",\n";
        first = false;
        ss << "    {\n";
        ss << "      \"id\": \""          << json_escape(e.id)                    << "\",\n";
        ss << "      \"type\": "           << (int)e.type                          << ",\n";
        ss << "      \"title\": \""        << json_escape(e.title)                 << "\",\n";
        ss << "      \"content\": \""      << json_escape(e.content_encrypted_b64) << "\",\n";
        ss << "      \"tags\": \""         << json_escape(e.tags)                  << "\",\n";
        ss << "      \"description\": \""  << json_escape(e.description)           << "\",\n";
        ss << "      \"language\": \""     << json_escape(e.language)              << "\",\n";
        ss << "      \"is_favorite\": "    << (e.is_favorite ? "true" : "false")   << ",\n";
        ss << "      \"is_dpapi\": "       << (e.is_dpapi    ? "true" : "false")   << ",\n";
        ss << "      \"created_at\": "     << e.created_at                         << ",\n";
        ss << "      \"updated_at\": "     << e.updated_at                         << "\n";
        ss << "    }";
    }
    ss << "\n  ]\n}\n";
    return ss.str();
}

static bool save_vault() {
    std::string json = serialize_vault();
    if (!write_file_bytes(g_vault_file, json)) {
        LOG_ERR("Failed to write vault file");
        return false;
    }
    return true;
}

static bool load_vault() {
    if (!file_exists(g_vault_file)) return true; /* first launch */

    std::string raw;
    if (!read_file_bytes(g_vault_file, raw)) {
        LOG_ERR("Failed to read vault file");
        return false;
    }

    JsonParser parser;
    JsonValue root;
    try {
        root = parser.parse(raw);
    } catch (std::exception& ex) {
        LOG_ERR((std::string("Vault JSON parse error: ") + ex.what()).c_str());
        return false;
    }

    if (root.kind != JsonValue::Object) {
        LOG_ERR("Vault root is not an object");
        return false;
    }

    /* Load password state */
    auto* nhp = root.get("notes_has_password");
    if (nhp && nhp->kind == JsonValue::Bool)
        g_notes_has_password = nhp->b;

    auto* ns = root.get("notes_salt");
    if (ns && ns->kind == JsonValue::String) {
        auto dec = base64_decode(ns->s);
        if (dec.size() == SALT_BYTES)
            memcpy(g_notes_salt, dec.data(), SALT_BYTES);
    }

    auto* nv = root.get("notes_verify");
    if (nv && nv->kind == JsonValue::String) {
        auto dec = base64_decode(nv->s);
        if (dec.size() == HMAC_BYTES)
            memcpy(g_notes_verify, dec.data(), HMAC_BYTES);
    }

    /* Load entries */
    auto* entries = root.get("entries");
    if (!entries || entries->kind != JsonValue::Array) return true;

    int skipped = 0;
    for (auto& ev : entries->arr) {
        if (ev.kind != JsonValue::Object) { ++skipped; continue; }

        EntryData e;
        try {
            auto* id = ev.get("id");
            auto* ty = ev.get("type");
            auto* tt = ev.get("title");

            if (!id || id->kind != JsonValue::String) throw std::runtime_error("missing id");
            if (!ty || ty->kind != JsonValue::Number)  throw std::runtime_error("missing type");
            if (!tt || tt->kind != JsonValue::String)  throw std::runtime_error("missing title");

            e.id    = id->s;
            e.type  = (QVEntryType)(int)ty->n;
            e.title = tt->s;

            auto* ct = ev.get("content");
            if (ct && ct->kind == JsonValue::String) e.content_encrypted_b64 = ct->s;

            auto* tg = ev.get("tags");
            if (tg && tg->kind == JsonValue::String) e.tags = tg->s;

            auto* ds = ev.get("description");
            if (ds && ds->kind == JsonValue::String) e.description = ds->s;

            auto* lg = ev.get("language");
            if (lg && lg->kind == JsonValue::String) e.language = lg->s;

            auto* fav = ev.get("is_favorite");
            if (fav && fav->kind == JsonValue::Bool) e.is_favorite = fav->b;

            auto* dp = ev.get("is_dpapi");
            if (dp && dp->kind == JsonValue::Bool) e.is_dpapi = dp->b;

            auto* ca = ev.get("created_at");
            if (ca && ca->kind == JsonValue::Number) e.created_at = ca->i64();

            auto* ua = ev.get("updated_at");
            if (ua && ua->kind == JsonValue::Number) e.updated_at = ua->i64();

            /* Validate type range */
            if (e.type < QV_TYPE_NOTE || e.type > QV_TYPE_COMMAND)
                throw std::runtime_error("invalid type");

        } catch (std::exception& ex) {
            LOG_WARN((std::string("Skipped corrupt entry: ") + ex.what()).c_str());
            ++skipped;
            continue;
        }

        g_entries[e.id] = std::move(e);
    }

    if (skipped > 0) {
        char buf[64];
        snprintf(buf, sizeof(buf), "Skipped %d corrupt entries on load", skipped);
        LOG_WARN(buf);
    }

    return true;
}

/* ------------------------------------------------------------------ */
/* File locking (advisory, same-machine protection)                    */
/* ------------------------------------------------------------------ */

static bool acquire_lock() {
    std::string lock_path = path_join(g_data_dir, "vault.lock");
    g_lock_handle = CreateFileA(
        lock_path.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        0, /* no sharing */
        nullptr,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_DELETE_ON_CLOSE,
        nullptr);
    return g_lock_handle != INVALID_HANDLE_VALUE;
}

static void release_lock() {
    if (g_lock_handle != INVALID_HANDLE_VALUE) {
        CloseHandle(g_lock_handle);
        g_lock_handle = INVALID_HANDLE_VALUE;
    }
}

/* ------------------------------------------------------------------ */
/* Public API implementation                                            */
/* ------------------------------------------------------------------ */

QVAPI const char* qv_version(void) {
    return QVAULT_VERSION_STR;
}

QVAPI const char* qv_error_string(QVResult code) {
    switch (code) {
        case QV_OK:                return "Success";
        case QV_ERR_NOT_INIT:      return "Vault not initialized";
        case QV_ERR_ALREADY_INIT:  return "Vault already initialized";
        case QV_ERR_BAD_ARG:       return "Invalid argument";
        case QV_ERR_NOT_FOUND:     return "Entry not found";
        case QV_ERR_IO:            return "I/O error";
        case QV_ERR_CORRUPT:       return "Corrupt data";
        case QV_ERR_CRYPTO:        return "Cryptographic error";
        case QV_ERR_BAD_PASSWORD:  return "Incorrect password";
        case QV_ERR_NO_PASSWORD:   return "No password set for this session";
        case QV_ERR_LOCKED:        return "Vault data file is locked by another process";
        case QV_ERR_DUPLICATE:     return "Duplicate entry ID";
        case QV_ERR_TOO_LARGE:     return "Content too large";
        case QV_ERR_INTERNAL:      return "Internal error";
        default:                   return "Unknown error";
    }
}

QVAPI QVResult qv_init(const char* data_dir, const char* log_dir) {
    std::lock_guard<std::mutex> lk(g_vault_mutex);

    if (g_initialized) return QV_ERR_ALREADY_INIT;

    /* Resolve data directory */
    if (data_dir && *data_dir) {
        g_data_dir = data_dir;
    } else {
        g_data_dir = path_join(get_appdata_path(),
                               "Quantum-suite-org\\QuantumVault");
    }

    /* Resolve log directory */
    std::string ldir;
    if (log_dir && *log_dir) {
        ldir = log_dir;
    } else {
        ldir = path_join(g_data_dir, "logs");
    }

    /* Create directories */
    if (!ensure_dir(g_data_dir)) return QV_ERR_IO;
    if (!ensure_dir(ldir)) return QV_ERR_IO;
    if (!ensure_dir(path_join(g_data_dir, "backups"))) return QV_ERR_IO;

    /* Open log file */
    g_log_dir = ldir;
    std::string log_path = path_join(ldir, "qvault.log");
    fopen_s(&g_log_file, log_path.c_str(), "a");

    LOG_INFO("qv_init starting");

    /* File paths */
    g_vault_file = path_join(g_data_dir, "vault.json");
    g_key_file   = path_join(g_data_dir, "vault.key");
    g_meta_file  = path_join(g_data_dir, "vault.meta");

    /* Acquire file lock */
    if (!acquire_lock()) {
        LOG_ERR("Could not acquire vault lock - another instance may be running");
        /* Non-fatal for CLI read-only operations; we proceed with a warning */
    }

    /* Load snippet/command key */
    if (!load_or_create_snippet_key()) {
        LOG_ERR("Failed to load/create snippet key");
        return QV_ERR_CRYPTO;
    }

    /* Load vault data */
    if (!load_vault()) {
        LOG_ERR("Failed to load vault");
        return QV_ERR_CORRUPT;
    }

    g_initialized = true;
    LOG_INFO("qv_init complete");
    return QV_OK;
}

QVAPI void qv_shutdown(void) {
    std::lock_guard<std::mutex> lk(g_vault_mutex);
    if (!g_initialized) return;

    save_vault();
    release_lock();

    g_entries.clear();
    secure_zero(g_snippet_key, sizeof(g_snippet_key));
    secure_zero(g_notes_key,   sizeof(g_notes_key));
    g_snippet_key_loaded = false;
    g_notes_unlocked     = false;
    g_notes_has_password = false;
    g_initialized        = false;

    if (g_log_file) { fclose(g_log_file); g_log_file = nullptr; }
    LOG_INFO("qv_shutdown");
}

QVAPI int qv_is_first_launch(void) {
    return file_exists(g_vault_file) ? 0 : 1;
}

/* ------------------------------------------------------------------ */
/* Password management                                                  */
/* ------------------------------------------------------------------ */

QVAPI QVResult qv_notes_set_password(const char* password) {
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!password || !*password) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    if (!g_notes_has_password) {
        /* First time: generate salt, derive key, store verify token */
        if (!cng_random(g_notes_salt, SALT_BYTES)) return QV_ERR_CRYPTO;

        uint8_t derived[AES_KEY_BYTES + HMAC_BYTES];
        if (!pbkdf2_sha256((const uint8_t*)password, strlen(password),
                           g_notes_salt, SALT_BYTES,
                           PBKDF2_ITER,
                           derived, sizeof(derived)))
            return QV_ERR_CRYPTO;

        memcpy(g_notes_key,   derived,               AES_KEY_BYTES);
        memcpy(g_notes_verify, derived + AES_KEY_BYTES, HMAC_BYTES);
        secure_zero(derived, sizeof(derived));

        g_notes_has_password = true;
        g_notes_unlocked     = true;
        save_vault();
        LOG_INFO("Notes password set for the first time");
        return QV_OK;
    }

    /* Verify against stored token */
    uint8_t derived[AES_KEY_BYTES + HMAC_BYTES];
    if (!pbkdf2_sha256((const uint8_t*)password, strlen(password),
                       g_notes_salt, SALT_BYTES,
                       PBKDF2_ITER,
                       derived, sizeof(derived)))
        return QV_ERR_CRYPTO;

    if (memcmp(derived + AES_KEY_BYTES, g_notes_verify, HMAC_BYTES) != 0) {
        secure_zero(derived, sizeof(derived));
        LOG_WARN("Incorrect notes password attempt");
        return QV_ERR_BAD_PASSWORD;
    }

    memcpy(g_notes_key, derived, AES_KEY_BYTES);
    secure_zero(derived, sizeof(derived));
    g_notes_unlocked = true;
    LOG_INFO("Notes unlocked");
    return QV_OK;
}

QVAPI int qv_notes_is_unlocked(void) {
    return g_notes_unlocked ? 1 : 0;
}

QVAPI QVResult qv_notes_change_password(const char* old_password,
                                          const char* new_password) {
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!old_password || !new_password) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    /* Verify old password */
    uint8_t derived_old[AES_KEY_BYTES + HMAC_BYTES];
    if (!pbkdf2_sha256((const uint8_t*)old_password, strlen(old_password),
                       g_notes_salt, SALT_BYTES, PBKDF2_ITER,
                       derived_old, sizeof(derived_old)))
        return QV_ERR_CRYPTO;

    if (memcmp(derived_old + AES_KEY_BYTES, g_notes_verify, HMAC_BYTES) != 0) {
        secure_zero(derived_old, sizeof(derived_old));
        return QV_ERR_BAD_PASSWORD;
    }

    uint8_t old_key[AES_KEY_BYTES];
    memcpy(old_key, derived_old, AES_KEY_BYTES);
    secure_zero(derived_old, sizeof(derived_old));

    /* Generate new salt and derive new key */
    uint8_t new_salt[SALT_BYTES];
    if (!cng_random(new_salt, SALT_BYTES)) return QV_ERR_CRYPTO;

    uint8_t derived_new[AES_KEY_BYTES + HMAC_BYTES];
    if (!pbkdf2_sha256((const uint8_t*)new_password, strlen(new_password),
                       new_salt, SALT_BYTES, PBKDF2_ITER,
                       derived_new, sizeof(derived_new)))
        return QV_ERR_CRYPTO;

    /* Re-encrypt all notes */
    for (auto& kv : g_entries) {
        auto& e = kv.second;
        if (e.type != QV_TYPE_NOTE) continue;
        if (e.content_encrypted_b64.empty()) continue;

        /* Decrypt with old key */
        /* Temporarily set old key */
        memcpy(g_notes_key, old_key, AES_KEY_BYTES);
        std::string plain;
        bool ok = decrypt_content(e.content_encrypted_b64, QV_TYPE_NOTE,
                                  e.is_dpapi, plain);
        if (!ok) {
            secure_zero(old_key, sizeof(old_key));
            secure_zero(derived_new, sizeof(derived_new));
            return QV_ERR_CRYPTO;
        }

        /* Re-encrypt with new key */
        memcpy(g_notes_key, derived_new, AES_KEY_BYTES);
        std::string new_enc = encrypt_content(plain, QV_TYPE_NOTE, e.is_dpapi);
        secure_zero(&plain[0], plain.size());

        if (new_enc.empty()) {
            secure_zero(old_key, sizeof(old_key));
            secure_zero(derived_new, sizeof(derived_new));
            return QV_ERR_CRYPTO;
        }
        e.content_encrypted_b64 = new_enc;
    }

    /* Commit */
    memcpy(g_notes_salt,   new_salt,                   SALT_BYTES);
    memcpy(g_notes_key,    derived_new,                 AES_KEY_BYTES);
    memcpy(g_notes_verify, derived_new + AES_KEY_BYTES, HMAC_BYTES);
    secure_zero(old_key, sizeof(old_key));
    secure_zero(derived_new, sizeof(derived_new));

    g_notes_unlocked = true;
    save_vault();
    LOG_INFO("Notes password changed and all notes re-encrypted");
    return QV_OK;
}

/* ------------------------------------------------------------------ */
/* Entry clone/free                                                     */
/* ------------------------------------------------------------------ */

QVAPI QVEntry* qv_entry_clone(const QVEntry* e) {
    if (!e) return nullptr;
    return new QVEntry(*e);
}

QVAPI void qv_entry_free(QVEntry* e) {
    if (!e) return;
    secure_zero(&e->content_decrypted[0], e->content_decrypted.size());
    delete e;
}

/* ------------------------------------------------------------------ */
/* Accessors                                                            */
/* ------------------------------------------------------------------ */

QVAPI const char* qv_entry_id(const QVEntry* e)          { return e ? e->data.id.c_str() : ""; }
QVAPI QVEntryType qv_entry_type(const QVEntry* e)         { return e ? e->data.type : QV_TYPE_NOTE; }
QVAPI const char* qv_entry_title(const QVEntry* e)        { return e ? e->data.title.c_str() : ""; }
QVAPI const char* qv_entry_content(const QVEntry* e)      { return e ? e->content_decrypted.c_str() : ""; }
QVAPI const char* qv_entry_tags(const QVEntry* e)         { return e ? e->data.tags.c_str() : ""; }
QVAPI const char* qv_entry_description(const QVEntry* e)  { return e ? e->data.description.c_str() : ""; }
QVAPI const char* qv_entry_language(const QVEntry* e)     { return e ? e->data.language.c_str() : ""; }
QVAPI int         qv_entry_is_favorite(const QVEntry* e)  { return e && e->data.is_favorite ? 1 : 0; }
QVAPI int         qv_entry_is_dpapi(const QVEntry* e)     { return e && e->data.is_dpapi    ? 1 : 0; }
QVAPI int64_t     qv_entry_created_at(const QVEntry* e)   { return e ? e->data.created_at : 0; }
QVAPI int64_t     qv_entry_updated_at(const QVEntry* e)   { return e ? e->data.updated_at : 0; }

/* ------------------------------------------------------------------ */
/* Internal: decrypt and populate an entry                             */
/* ------------------------------------------------------------------ */

static QVResult populate_entry(QVEntry& entry) {
    const auto& e = entry.data;

    if (e.content_encrypted_b64.empty()) {
        entry.content_decrypted = "";
        return QV_OK;
    }

    if (e.type == QV_TYPE_NOTE && !g_notes_unlocked) {
        entry.content_decrypted = "[Locked - enter password to view]";
        return QV_OK;
    }

    std::string plain;
    if (!decrypt_content(e.content_encrypted_b64, e.type, e.is_dpapi, plain)) {
        entry.content_decrypted = "[Decryption failed]";
        LOG_ERR("Failed to decrypt entry content");
        return QV_ERR_CRYPTO;
    }

    entry.content_decrypted = plain;
    secure_zero(&plain[0], plain.size());
    return QV_OK;
}

/* ------------------------------------------------------------------ */
/* CRUD                                                                 */
/* ------------------------------------------------------------------ */

/* Thread-local storage for last returned ID string */
static thread_local std::string tl_last_id;

QVAPI QVResult qv_entry_create(
    QVEntryType  type,
    const char*  title,
    const char*  content,
    const char*  tags,
    const char*  description,
    const char*  language,
    int          use_dpapi,
    const char** out_id)
{
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!title || !*title) return QV_ERR_BAD_ARG;
    if (strlen(title) > 512) return QV_ERR_TOO_LARGE;

    if (type == QV_TYPE_NOTE && !g_notes_unlocked && content && *content)
        return QV_ERR_NO_PASSWORD;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    EntryData e;
    e.id          = gen_uuid();
    e.type        = type;
    e.title       = title;
    e.tags        = tags        ? tags        : "";
    e.description = description ? description : "";
    e.language    = language    ? language    : "";
    e.is_favorite = false;
    e.is_dpapi    = (type == QV_TYPE_NOTE && use_dpapi) ? true : false;
    e.created_at  = now_unix();
    e.updated_at  = e.created_at;

    if (content && *content) {
        std::string enc = encrypt_content(content, type, e.is_dpapi);
        if (enc.empty()) return QV_ERR_CRYPTO;
        e.content_encrypted_b64 = enc;
    }

    tl_last_id = e.id;
    g_entries[e.id] = std::move(e);

    if (!save_vault()) return QV_ERR_IO;

    if (out_id) *out_id = tl_last_id.c_str();
    return QV_OK;
}

QVAPI QVResult qv_entry_update(
    const char* id,
    const char* title,
    const char* content,
    const char* tags,
    const char* description,
    const char* language,
    int         use_dpapi)
{
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!id || !*id) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    auto it = g_entries.find(id);
    if (it == g_entries.end()) return QV_ERR_NOT_FOUND;

    auto& e = it->second;

    if (title)       e.title       = title;
    if (tags)        e.tags        = tags;
    if (description) e.description = description;
    if (language)    e.language    = language;
    if (use_dpapi >= 0 && e.type == QV_TYPE_NOTE)
        e.is_dpapi = (use_dpapi != 0);

    if (content) {
        if (e.type == QV_TYPE_NOTE && !g_notes_unlocked)
            return QV_ERR_NO_PASSWORD;
        std::string enc = encrypt_content(content, e.type, e.is_dpapi);
        if (enc.empty()) return QV_ERR_CRYPTO;
        e.content_encrypted_b64 = enc;
    }

    e.updated_at = now_unix();

    if (!save_vault()) return QV_ERR_IO;
    return QV_OK;
}

QVAPI QVResult qv_entry_delete(const char* id) {
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!id || !*id) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    auto it = g_entries.find(id);
    if (it == g_entries.end()) return QV_ERR_NOT_FOUND;

    g_entries.erase(it);
    if (!save_vault()) return QV_ERR_IO;
    return QV_OK;
}

QVAPI QVResult qv_entry_duplicate(const char* id, const char** out_id) {
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!id || !*id) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    auto it = g_entries.find(id);
    if (it == g_entries.end()) return QV_ERR_NOT_FOUND;

    EntryData copy = it->second;
    copy.id         = gen_uuid();
    copy.title      += " (copy)";
    copy.created_at = now_unix();
    copy.updated_at = copy.created_at;
    copy.is_favorite = false;

    tl_last_id = copy.id;
    g_entries[copy.id] = std::move(copy);

    if (!save_vault()) return QV_ERR_IO;
    if (out_id) *out_id = tl_last_id.c_str();
    return QV_OK;
}

QVAPI QVResult qv_entry_set_favorite(const char* id, int is_favorite) {
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!id || !*id) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    auto it = g_entries.find(id);
    if (it == g_entries.end()) return QV_ERR_NOT_FOUND;

    it->second.is_favorite = (is_favorite != 0);
    it->second.updated_at  = now_unix();

    if (!save_vault()) return QV_ERR_IO;
    return QV_OK;
}

/* ------------------------------------------------------------------ */
/* Retrieval                                                            */
/* ------------------------------------------------------------------ */

/* Pool of entry objects for list results (freed per-result-set) */
static std::vector<std::unique_ptr<QVEntry>> g_entry_pool;

static QVEntry* make_entry_handle(const EntryData& data) {
    auto* e = new QVEntry();
    e->data = data;
    populate_entry(*e);
    return e;
}

QVAPI void qv_list_result_free(QVListResult* r) {
    if (!r) return;
    if (r->entries) {
        for (int i = 0; i < r->count; ++i)
            delete r->entries[i];
        delete[] r->entries;
        r->entries = nullptr;
    }
    r->count = 0;
}

QVAPI QVResult qv_entry_get(const char* id, const QVEntry** out_entry) {
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!id || !out_entry) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    auto it = g_entries.find(id);
    if (it == g_entries.end()) return QV_ERR_NOT_FOUND;

    /* We store a single static entry for get (non-list). Callers that need
       persistent access must use qv_entry_clone(). */
    static thread_local QVEntry tl_get_entry;
    tl_get_entry.data = it->second;
    populate_entry(tl_get_entry);
    *out_entry = &tl_get_entry;
    return QV_OK;
}

static bool str_icontains(const std::string& haystack, const std::string& needle) {
    if (needle.empty()) return true;
    auto it = std::search(
        haystack.begin(), haystack.end(),
        needle.begin(),   needle.end(),
        [](char a, char b){ return std::tolower((unsigned char)a) ==
                                   std::tolower((unsigned char)b); });
    return it != haystack.end();
}

QVAPI QVResult qv_entry_list(
    int           type,
    int           favorites_only,
    int           limit,
    int           offset,
    const char*   sort_by,
    QVListResult* out)
{
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!out) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    std::vector<const EntryData*> matches;
    for (auto& kv : g_entries) {
        const auto& e = kv.second;
        if (type >= 0 && (int)e.type != type) continue;
        if (favorites_only && !e.is_favorite) continue;
        matches.push_back(&e);
    }

    /* Sort */
    std::string sort = sort_by ? sort_by : "updated";
    if (sort == "created") {
        std::sort(matches.begin(), matches.end(), [](auto* a, auto* b){
            return a->created_at > b->created_at; });
    } else if (sort == "title") {
        std::sort(matches.begin(), matches.end(), [](auto* a, auto* b){
            return a->title < b->title; });
    } else {
        std::sort(matches.begin(), matches.end(), [](auto* a, auto* b){
            return a->updated_at > b->updated_at; });
    }

    /* Pagination */
    if (offset > 0 && (size_t)offset < matches.size())
        matches.erase(matches.begin(), matches.begin() + offset);
    if (limit > 0 && (int)matches.size() > limit)
        matches.resize((size_t)limit);

    out->count   = (int)matches.size();
    out->entries = out->count > 0 ? new QVEntry*[out->count] : nullptr;
    for (int i = 0; i < out->count; ++i)
        out->entries[i] = make_entry_handle(*matches[i]);

    return QV_OK;
}

QVAPI QVResult qv_entry_search(
    const char*   query,
    int           type,
    int           search_content,
    int           limit,
    QVListResult* out)
{
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!out) return QV_ERR_BAD_ARG;

    std::string q = query ? query : "";

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    std::vector<const EntryData*> matches;
    for (auto& kv : g_entries) {
        const auto& e = kv.second;
        if (type >= 0 && (int)e.type != type) continue;

        bool hit = str_icontains(e.title, q) ||
                   str_icontains(e.tags,  q) ||
                   str_icontains(e.description, q);

        if (!hit && search_content && e.type != QV_TYPE_NOTE) {
            /* Decrypt snippet/command content for search */
            std::string plain;
            if (decrypt_content(e.content_encrypted_b64, e.type, false, plain))
                hit = str_icontains(plain, q);
        }

        if (hit) matches.push_back(&e);
    }

    /* Sort by relevance (title match first, then updated) */
    std::sort(matches.begin(), matches.end(), [&q](auto* a, auto* b) {
        bool ta = str_icontains(a->title, q);
        bool tb = str_icontains(b->title, q);
        if (ta != tb) return ta > tb;
        return a->updated_at > b->updated_at;
    });

    if (limit > 0 && (int)matches.size() > limit)
        matches.resize((size_t)limit);

    out->count   = (int)matches.size();
    out->entries = out->count > 0 ? new QVEntry*[out->count] : nullptr;
    for (int i = 0; i < out->count; ++i)
        out->entries[i] = make_entry_handle(*matches[i]);

    return QV_OK;
}

QVAPI QVResult qv_entry_recent(int limit, QVListResult* out) {
    return qv_entry_list(-1, 0, limit > 0 ? limit : 20, 0, "updated", out);
}

/* ------------------------------------------------------------------ */
/* Export / Import                                                      */
/* ------------------------------------------------------------------ */

QVAPI QVResult qv_export_json(const char* path, int include_encrypted) {
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!path || !*path) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    std::ostringstream ss;
    ss << "{\n";
    ss << "  \"format\": \"qvault-export\",\n";
    ss << "  \"format_version\": 1,\n";
    ss << "  \"qvault_version\": \"" << QVAULT_VERSION_STR << "\",\n";
    ss << "  \"exported_at\": " << now_unix() << ",\n";
    ss << "  \"note\": \"Content in this export is PLAINTEXT. Keep this file secure.\",\n";
    ss << "  \"entries\": [\n";

    bool first = true;
    for (auto& kv : g_entries) {
        const auto& e = kv.second;
        if (!first) ss << ",\n";
        first = false;

        std::string plain_content;
        if (include_encrypted && !e.content_encrypted_b64.empty()) {
            if (e.type == QV_TYPE_NOTE && !g_notes_unlocked) {
                plain_content = "";
            } else {
                decrypt_content(e.content_encrypted_b64, e.type, e.is_dpapi, plain_content);
            }
        }

        ss << "    {\n";
        ss << "      \"id\": \""          << json_escape(e.id)          << "\",\n";
        ss << "      \"type\": "           << (int)e.type                << ",\n";
        ss << "      \"type_name\": \""    << (e.type == QV_TYPE_NOTE ? "note" :
                                               e.type == QV_TYPE_SNIPPET ? "snippet" : "command")
                                                                         << "\",\n";
        ss << "      \"title\": \""        << json_escape(e.title)       << "\",\n";
        ss << "      \"content\": \""      << json_escape(plain_content) << "\",\n";
        ss << "      \"tags\": \""         << json_escape(e.tags)        << "\",\n";
        ss << "      \"description\": \""  << json_escape(e.description) << "\",\n";
        ss << "      \"language\": \""     << json_escape(e.language)    << "\",\n";
        ss << "      \"is_favorite\": "    << (e.is_favorite ? "true" : "false") << ",\n";
        ss << "      \"created_at\": "     << e.created_at               << ",\n";
        ss << "      \"updated_at\": "     << e.updated_at               << "\n";
        ss << "    }";
    }

    ss << "\n  ]\n}\n";

    if (!write_file_bytes(path, ss.str())) return QV_ERR_IO;
    LOG_INFO((std::string("Exported vault to: ") + path).c_str());
    return QV_OK;
}

QVAPI QVResult qv_import_json(
    const char* path,
    int         merge,
    int*        out_imported,
    int*        out_skipped,
    int*        out_errors)
{
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!path || !*path) return QV_ERR_BAD_ARG;

    std::string raw;
    if (!read_file_bytes(path, raw)) return QV_ERR_IO;

    JsonParser parser;
    JsonValue root;
    try {
        root = parser.parse(raw);
    } catch (...) {
        return QV_ERR_CORRUPT;
    }

    if (root.kind != JsonValue::Object) return QV_ERR_CORRUPT;

    auto* entries = root.get("entries");
    if (!entries || entries->kind != JsonValue::Array) return QV_ERR_CORRUPT;

    int imported = 0, skipped = 0, errors = 0;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    if (!merge) g_entries.clear();

    for (auto& ev : entries->arr) {
        if (ev.kind != JsonValue::Object) { ++errors; continue; }

        EntryData e;
        try {
            auto* id = ev.get("id");
            auto* ty = ev.get("type");
            auto* tt = ev.get("title");

            if (!id || !tt) throw std::runtime_error("missing required field");

            e.id    = id->s;
            e.type  = ty ? (QVEntryType)(int)ty->n : QV_TYPE_NOTE;
            e.title = tt->s;

            auto* ct = ev.get("content");
            std::string plain_content = (ct && ct->kind == JsonValue::String) ? ct->s : "";

            auto* tg = ev.get("tags");
            if (tg) e.tags = tg->s;

            auto* ds = ev.get("description");
            if (ds) e.description = ds->s;

            auto* lg = ev.get("language");
            if (lg) e.language = lg->s;

            auto* fav = ev.get("is_favorite");
            if (fav && fav->kind == JsonValue::Bool) e.is_favorite = fav->b;

            auto* ca = ev.get("created_at");
            if (ca && ca->kind == JsonValue::Number) e.created_at = ca->i64();
            else e.created_at = now_unix();

            auto* ua = ev.get("updated_at");
            if (ua && ua->kind == JsonValue::Number) e.updated_at = ua->i64();
            else e.updated_at = now_unix();

            /* Re-encrypt the plaintext content */
            if (!plain_content.empty()) {
                if (e.type == QV_TYPE_NOTE && !g_notes_unlocked) {
                    e.content_encrypted_b64 = ""; /* skip note content */
                } else {
                    std::string enc = encrypt_content(plain_content, e.type, false);
                    if (enc.empty()) throw std::runtime_error("encryption failed");
                    e.content_encrypted_b64 = enc;
                }
            }

        } catch (...) {
            ++errors;
            continue;
        }

        if (merge && g_entries.count(e.id)) {
            ++skipped;
            continue;
        }

        if (merge && g_entries.count(e.id) == 0) {
            /* new id needed if importing to avoid collision */
        }

        g_entries[e.id] = std::move(e);
        ++imported;
    }

    save_vault();

    if (out_imported) *out_imported = imported;
    if (out_skipped)  *out_skipped  = skipped;
    if (out_errors)   *out_errors   = errors;

    LOG_INFO((std::string("Import complete: ") + std::to_string(imported) +
              " imported, " + std::to_string(skipped) + " skipped, " +
              std::to_string(errors) + " errors").c_str());
    return QV_OK;
}

/* ------------------------------------------------------------------ */
/* Backup                                                               */
/* ------------------------------------------------------------------ */

static thread_local std::string tl_backup_path;

QVAPI QVResult qv_backup(const char* backup_dir, const char** out_path) {
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!file_exists(g_vault_file)) return QV_ERR_NOT_FOUND;

    std::string bdir = backup_dir ? backup_dir : path_join(g_data_dir, "backups");
    if (!ensure_dir(bdir)) return QV_ERR_IO;

    SYSTEMTIME st;
    GetLocalTime(&st);
    char timestamp[32];
    snprintf(timestamp, sizeof(timestamp), "%04d%02d%02d_%02d%02d%02d",
             st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);

    std::string dest = path_join(bdir, std::string("vault_backup_") + timestamp + ".json");

    std::string raw;
    if (!read_file_bytes(g_vault_file, raw)) return QV_ERR_IO;
    if (!write_file_bytes(dest, raw)) return QV_ERR_IO;

    tl_backup_path = dest;
    if (out_path) *out_path = tl_backup_path.c_str();

    LOG_INFO((std::string("Backup created: ") + dest).c_str());
    return QV_OK;
}

/* ------------------------------------------------------------------ */
/* Stats                                                                */
/* ------------------------------------------------------------------ */

QVAPI QVResult qv_get_stats(QVStats* out) {
    if (!g_initialized) return QV_ERR_NOT_INIT;
    if (!out) return QV_ERR_BAD_ARG;

    std::lock_guard<std::mutex> lk(g_vault_mutex);

    memset(out, 0, sizeof(*out));
    for (auto& kv : g_entries) {
        ++out->total_entries;
        switch (kv.second.type) {
            case QV_TYPE_NOTE:    ++out->note_count;    break;
            case QV_TYPE_SNIPPET: ++out->snippet_count; break;
            case QV_TYPE_COMMAND: ++out->command_count; break;
        }
        if (kv.second.is_favorite) ++out->favorite_count;
    }

    if (file_exists(g_vault_file)) {
        WIN32_FILE_ATTRIBUTE_DATA fa;
        if (GetFileAttributesExA(g_vault_file.c_str(), GetFileExInfoStandard, &fa)) {
            out->vault_file_size_bytes =
                ((int64_t)fa.nFileSizeHigh << 32) | fa.nFileSizeLow;
        }
    }

    return QV_OK;
}

QVAPI const char* qv_get_data_dir(void) {
    return g_data_dir.c_str();
}

QVAPI const char* qv_get_log_dir(void) {
    return g_log_dir.c_str();
}
