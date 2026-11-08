// ==WindhawkMod==
// @id              refilebrowser-explorer-redirect
// @name            Redirect explorer.exe folder launches to ReFileBrowser
// @description     When a program runs "explorer.exe <folder>" or "explorer.exe /select,<file>", open it in ReFileBrowser instead
// @version         1.1
// @author          ReFileBrowser
// @include         *
// @exclude         ReFileBrowser.exe
// @exclude         conhost.exe
// @exclude         csrss.exe
// @exclude         smss.exe
// @exclude         lsass.exe
// @exclude         winlogon.exe
// @exclude         services.exe
// @exclude         windhawk.exe
// @exclude         windhawk-ui.exe
// ==/WindhawkMod==

// ==WindhawkModReadme==
/*
# Redirect explorer.exe folder launches to ReFileBrowser

Many programs (Steam, game launchers, wallpaper tools, ...) open folders by
starting `explorer.exe` directly, e.g. `explorer.exe /n,/select,C:\file.txt`.
That never looks at the Folder verbs in the registry, so a replacement file
manager cannot catch it there. The new explorer.exe hands the request to the
running shell within ~100 ms, too fast to hook inside it.

So this mod hooks `CreateProcessInternalW` in the *calling* program. When the
image is explorer.exe and the arguments are an existing file-system folder, or
`/select,` an existing item, the call starts ReFileBrowser instead.

Left to Explorer: `shell:` / `::{CLSID}` / URL targets, COM launches
(`/factory`, `-Embedding`), unknown switches, and the shell's own start-up
(no taskbar yet).

Windhawk does not inject into elevated processes or into paths it treats as games
(for example `*\steamapps\common\*`). To cover such a program, add its exe to
Windhawk's process inclusion list.

Optional decision log: `%TEMP%\refilebrowser-redirect.log`.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- exePath: ""
  $name: ReFileBrowser.exe path
  $description: Leave empty to use the path registered by "ReFileBrowser.exe --register"
- redirectPlainLaunch: true
  $name: Also redirect "explorer.exe" with no arguments
  $description: While the shell is running, a plain explorer.exe opens a new Explorer window
- log: false
  $name: Log decisions to %TEMP%\refilebrowser-redirect.log
*/
// ==/WindhawkModSettings==

#include <windows.h>

#include <string>
#include <vector>

namespace {

std::wstring g_exe;
bool g_plainLaunch = true;
bool g_log = true;

std::wstring Trim(const std::wstring& s) {
    size_t b = 0, e = s.size();
    while (b < e && (s[b] == L' ' || s[b] == L'\t')) b++;
    while (e > b && (s[e - 1] == L' ' || s[e - 1] == L'\t')) e--;
    return s.substr(b, e - b);
}

std::wstring Lower(std::wstring s) {
    for (auto& c : s) c = (wchar_t)towlower(c);
    return s;
}

void FileLog(const std::wstring& line) {
    if (!g_log) return;
    wchar_t path[MAX_PATH];
    DWORD n = GetTempPathW(MAX_PATH, path);
    if (n == 0 || n > MAX_PATH - 32) return;
    wcscat_s(path, L"refilebrowser-redirect.log");
    HANDLE h = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME st;
    GetLocalTime(&st);
    wchar_t self[MAX_PATH] = L"?";
    GetModuleFileNameW(nullptr, self, MAX_PATH);
    const wchar_t* base = wcsrchr(self, L'\\');
    wchar_t prefix[MAX_PATH + 64];
    swprintf_s(prefix, L"%02d:%02d:%02d.%03d %s(%u) ", st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
               base ? base + 1 : self, GetCurrentProcessId());
    std::wstring w = prefix + line + L"\r\n";
    int len = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string u(len, 0);
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(), u.data(), len, nullptr, nullptr);
    DWORD written;
    WriteFile(h, u.data(), (DWORD)u.size(), &written, nullptr);
    CloseHandle(h);
}

// First token of a command line (the program), and a pointer to what follows it.
std::wstring FirstToken(const wchar_t* cmd, const wchar_t** rest) {
    const wchar_t* p = cmd;
    while (*p == L' ' || *p == L'\t') p++;
    std::wstring tok;
    if (*p == L'"') {
        p++;
        while (*p && *p != L'"') tok.push_back(*p++);
        if (*p) p++;
    } else {
        while (*p && *p != L' ' && *p != L'\t') tok.push_back(*p++);
    }
    if (rest) *rest = p;
    return tok;
}

bool IsExplorerImage(const std::wstring& path) {
    std::wstring l = Lower(path);
    size_t slash = l.find_last_of(L"\\/");
    std::wstring base = slash == std::wstring::npos ? l : l.substr(slash + 1);
    return base == L"explorer.exe" || base == L"explorer";
}

// Explorer's own syntax: switches and the path are separated by commas, quotes protect commas.
std::vector<std::wstring> SplitExplorerArgs(const wchar_t* p) {
    std::vector<std::wstring> out;
    std::wstring cur;
    bool quoted = false;
    auto flush = [&] {
        std::wstring t = Trim(cur);
        cur.clear();
        if (t.empty()) return;
        if (t[0] == L'/' || t[0] == L'-') {  // "/select C:\x" written with a space
            size_t sp = t.find_first_of(L" \t");
            if (sp != std::wstring::npos) {
                out.push_back(t.substr(0, sp));
                std::wstring r = Trim(t.substr(sp));
                if (!r.empty()) out.push_back(r);
                return;
            }
        }
        out.push_back(t);
    };
    for (; *p; p++) {
        if (*p == L'"') { quoted = !quoted; continue; }
        if (*p == L',' && !quoted) { flush(); continue; }
        cur.push_back(*p);
    }
    flush();
    return out;
}

bool ShellRunning() { return FindWindowW(L"Shell_TrayWnd", nullptr) != nullptr; }

std::wstring FullPath(const std::wstring& raw, const wchar_t* cwd) {
    wchar_t expanded[MAX_PATH * 4];
    DWORD n = ExpandEnvironmentStringsW(raw.c_str(), expanded, ARRAYSIZE(expanded));
    std::wstring s = (n > 0 && n <= ARRAYSIZE(expanded)) ? std::wstring(expanded) : raw;
    bool rooted = (s.size() >= 2 && s[1] == L':') || (s.size() >= 2 && s[0] == L'\\' && s[1] == L'\\');
    if (!rooted && cwd && *cwd) {
        s = std::wstring(cwd) + (cwd[wcslen(cwd) - 1] == L'\\' ? L"" : L"\\") + s;
    }
    wchar_t full[MAX_PATH * 4];
    DWORD m = GetFullPathNameW(s.c_str(), ARRAYSIZE(full), full, nullptr);
    return (m > 0 && m < ARRAYSIZE(full)) ? std::wstring(full) : s;
}

// Quote one argument for CommandLineToArgvW / .NET; a trailing backslash ("E:\") must be doubled.
std::wstring Quote(const std::wstring& s) {
    std::wstring q = L"\"" + s;
    if (!s.empty() && s.back() == L'\\') q += L'\\';
    return q + L"\"";
}

// Builds the ReFileBrowser command line, or returns false to let Explorer handle the launch.
bool BuildRedirect(const wchar_t* argsText, const wchar_t* cwd, std::wstring* cmd, std::wstring* why) {
    bool select = false;
    std::wstring target;
    for (const auto& a : SplitExplorerArgs(argsText)) {
        if (a[0] == L'/' || a[0] == L'-') {
            std::wstring sw = Lower(a);
            if (sw == L"/n" || sw == L"/e" || sw == L"/separate" || sw == L"/root") continue;
            if (sw == L"/select") { select = true; continue; }
            *why = L"switch " + a;
            return false;
        }
        if (!target.empty()) { *why = L"two targets"; return false; }
        target = a;
    }

    if (target.empty()) {
        if (select || !g_plainLaunch) { *why = L"no target"; return false; }
        *cmd = Quote(g_exe);
        return true;
    }

    std::wstring full = FullPath(target, cwd);
    bool fsPath = (full.size() >= 3 && full[1] == L':' && full[2] == L'\\') ||
                  (full.size() >= 3 && full[0] == L'\\' && full[1] == L'\\' && full[2] != L'?' && full[2] != L'.');
    if (!fsPath) { *why = L"not a file-system path: " + target; return false; }

    DWORD attr = GetFileAttributesW(full.c_str());
    if (attr == INVALID_FILE_ATTRIBUTES) { *why = L"does not exist: " + full; return false; }
    bool isDir = (attr & FILE_ATTRIBUTE_DIRECTORY) != 0;
    if (!select && !isDir) { *why = L"file without /select (opens the file)"; return false; }

    *cmd = Quote(g_exe) + (select ? L" --select " : L" ") + Quote(full);
    return true;
}

using CreateProcessInternalW_t = BOOL(WINAPI*)(HANDLE, LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES,
                                               BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW, LPPROCESS_INFORMATION,
                                               PHANDLE);
CreateProcessInternalW_t CreateProcessInternalW_Original;

thread_local bool g_inHook;

BOOL WINAPI CreateProcessInternalW_Hook(HANDLE token, LPCWSTR app, LPWSTR cmdLine, LPSECURITY_ATTRIBUTES pa,
                                        LPSECURITY_ATTRIBUTES ta, BOOL inherit, DWORD flags, LPVOID env,
                                        LPCWSTR cwd, LPSTARTUPINFOW si, LPPROCESS_INFORMATION pi, PHANDLE newToken) {
    if (!g_inHook && !g_exe.empty() && (app || cmdLine)) {
        g_inHook = true;
        const wchar_t* rest = L"";
        std::wstring first = cmdLine ? FirstToken(cmdLine, &rest) : L"";
        std::wstring image = app ? std::wstring(app) : first;
        if (IsExplorerImage(image) && ShellRunning() && GetFileAttributesW(g_exe.c_str()) != INVALID_FILE_ATTRIBUTES) {
            // With lpApplicationName set, the command line may or may not start with the program name.
            const wchar_t* args = (cmdLine && IsExplorerImage(first)) ? rest : (cmdLine ? cmdLine : L"");
            std::wstring newCmd, why;
            if (BuildRedirect(args, cwd, &newCmd, &why)) {
                FileLog(L"redirect: " + std::wstring(cmdLine ? cmdLine : app) + L"  ->  " + newCmd);
                std::vector<wchar_t> buf(newCmd.begin(), newCmd.end());
                buf.push_back(0);
                BOOL ok = CreateProcessInternalW_Original(token, g_exe.c_str(), buf.data(), pa, ta, inherit, flags, env,
                                                          nullptr, si, pi, newToken);
                if (ok) {
                    AllowSetForegroundWindow(pi->dwProcessId);
                    g_inHook = false;
                    return ok;
                }
                FileLog(L"redirect failed, error " + std::to_wstring(GetLastError()) + L"; falling back to Explorer");
            } else {
                FileLog(L"pass: " + std::wstring(cmdLine ? cmdLine : app) + L"  (" + why + L")");
            }
        }
        g_inHook = false;
    }
    return CreateProcessInternalW_Original(token, app, cmdLine, pa, ta, inherit, flags, env, cwd, si, pi, newToken);
}

// Path written by "ReFileBrowser.exe --register":
// HKCU\Software\Classes\Folder\shell\ReFileBrowser\command = "C:\...\ReFileBrowser.exe" "%1"
std::wstring RegisteredExe() {
    wchar_t buf[MAX_PATH * 2];
    DWORD cb = sizeof(buf);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Classes\\Folder\\shell\\ReFileBrowser\\command", nullptr,
                     RRF_RT_REG_SZ, nullptr, buf, &cb) != ERROR_SUCCESS) {
        return L"";
    }
    return FirstToken(buf, nullptr);
}

void LoadSettings() {
    PCWSTR exe = Wh_GetStringSetting(L"exePath");
    g_exe = exe ? exe : L"";
    Wh_FreeStringSetting(exe);
    if (g_exe.empty()) g_exe = RegisteredExe();
    g_plainLaunch = Wh_GetIntSetting(L"redirectPlainLaunch") != 0;
    g_log = Wh_GetIntSetting(L"log") != 0;
}

}  // namespace

BOOL Wh_ModInit() {
    LoadSettings();
    HMODULE kb = GetModuleHandleW(L"kernelbase.dll");
    void* target = kb ? (void*)GetProcAddress(kb, "CreateProcessInternalW") : nullptr;
    if (!target) return FALSE;
    return Wh_SetFunctionHook(target, (void*)CreateProcessInternalW_Hook, (void**)&CreateProcessInternalW_Original);
}

void Wh_ModSettingsChanged() { LoadSettings(); }
