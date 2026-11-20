#include "windows_dispatch_types.h"

typedef void (__stdcall *ApiEntry)(void);
extern void __stdcall resolve_api(const char *module, const char *name, void *slot,
                                  void *module_handle, ApiEntry fallback);

extern const char module_2df620[];
extern const char module_2df640[];
extern const char module_2df758[];
extern const char module_2df8d0[];
extern void *handle_451a68;
extern void *handle_451a6c;
extern void *handle_451a70;
extern void *handle_451a74;
extern void *handle_451a78;
extern void *handle_451a7c;
extern void *handle_451a80;
extern void *handle_451a84;
extern void *handle_451a88;
extern void *handle_451a8c;
extern void *handle_451a90;
extern void *handle_451a94;
extern void *handle_451a9c;
extern void *handle_451aa0;
extern void *handle_451aa8;
extern void *handle_451aac;
extern void *handle_451ab0;
extern void *handle_451ab8;
extern void *handle_451abc;
extern void *handle_451ac0;
extern void *handle_451ac4;
extern void *handle_451ac8;
extern void *handle_451ad0;
extern void *handle_451ad4;
extern void *handle_451ad8;
extern void *handle_451adc;
extern void *handle_451ae0;
extern void *handle_451ae4;
extern void *handle_451afc;
extern void *handle_451b00;
extern void *handle_451b04;
extern void *handle_451b0c;
extern void *handle_451b10;
extern void *handle_451b14;
extern void *handle_451b18;
extern void *handle_451b1c;
extern void *handle_451b24;
extern void *handle_451b28;
extern void *handle_451b2c;
extern void *handle_451b30;
extern void *handle_451b34;
extern void *handle_451b38;
extern void *handle_451b3c;
extern void *handle_451b40;
extern void *handle_451b44;
extern void *handle_451b48;
extern void *handle_451b50;
extern void *handle_451b54;
extern void *handle_451b58;
extern void *handle_451b5c;
extern void *handle_451b60;
extern void *handle_451b64;
extern void *handle_451b6c;
extern void *handle_451b70;
extern void *handle_451b74;
extern void *handle_451b78;
extern void *handle_451b7c;
extern void *handle_451b80;
extern void *handle_451b84;
extern void *handle_451b88;
extern void *handle_451b8c;
extern void *handle_451b90;

extern const char name_DispatchMessageW[];
extern LRESULT (__stdcall *slot_DispatchMessageW)(const MSG *);
extern LRESULT __stdcall fallback_DispatchMessageW(const MSG *);
LRESULT __stdcall dispatch_DispatchMessageW(const MSG * lpMsg)
{
    resolve_api(module_2df620, name_DispatchMessageW, &slot_DispatchMessageW, handle_451b90, (ApiEntry)fallback_DispatchMessageW);
    return slot_DispatchMessageW(lpMsg);
}

extern const char name_CreateMutexW[];
extern HANDLE (__stdcall *slot_CreateMutexW)(LPSECURITY_ATTRIBUTES, BOOL, LPCWSTR);
extern HANDLE __stdcall fallback_CreateMutexW(LPSECURITY_ATTRIBUTES, BOOL, LPCWSTR);
HANDLE __stdcall dispatch_CreateMutexW(LPSECURITY_ATTRIBUTES lpMutexAttributes, BOOL bInitialOwner, LPCWSTR lpName)
{
    resolve_api(module_2df640, name_CreateMutexW, &slot_CreateMutexW, handle_451b8c, (ApiEntry)fallback_CreateMutexW);
    return slot_CreateMutexW(lpMutexAttributes, bInitialOwner, lpName);
}

extern const char name_FindFirstFileW[];
extern HANDLE (__stdcall *slot_FindFirstFileW)(LPCWSTR, LPWIN32_FIND_DATAW);
extern HANDLE __stdcall fallback_FindFirstFileW(LPCWSTR, LPWIN32_FIND_DATAW);
HANDLE __stdcall dispatch_FindFirstFileW(LPCWSTR lpFileName, LPWIN32_FIND_DATAW lpFindFileData)
{
    resolve_api(module_2df640, name_FindFirstFileW, &slot_FindFirstFileW, handle_451b88, (ApiEntry)fallback_FindFirstFileW);
    return slot_FindFirstFileW(lpFileName, lpFindFileData);
}

extern const char name_SendMessageW[];
extern LRESULT (__stdcall *slot_SendMessageW)(HWND, UINT, WPARAM, LPARAM);
extern LRESULT __stdcall fallback_SendMessageW(HWND, UINT, WPARAM, LPARAM);
LRESULT __stdcall dispatch_SendMessageW(HWND hWnd, UINT Msg, WPARAM wParam, LPARAM lParam)
{
    resolve_api(module_2df620, name_SendMessageW, &slot_SendMessageW, handle_451b84, (ApiEntry)fallback_SendMessageW);
    return slot_SendMessageW(hWnd, Msg, wParam, lParam);
}

extern const char name_MessageBoxW[];
extern int (__stdcall *slot_MessageBoxW)(HWND, LPCWSTR, LPCWSTR, UINT);
extern int __stdcall fallback_MessageBoxW(HWND, LPCWSTR, LPCWSTR, UINT);
int __stdcall dispatch_MessageBoxW(HWND hWnd, LPCWSTR lpText, LPCWSTR lpCaption, UINT uType)
{
    resolve_api(module_2df620, name_MessageBoxW, &slot_MessageBoxW, handle_451b80, (ApiEntry)fallback_MessageBoxW);
    return slot_MessageBoxW(hWnd, lpText, lpCaption, uType);
}

extern const char name_CreateDirectoryW[];
extern BOOL (__stdcall *slot_CreateDirectoryW)(LPCWSTR, LPSECURITY_ATTRIBUTES);
extern BOOL __stdcall fallback_CreateDirectoryW(LPCWSTR, LPSECURITY_ATTRIBUTES);
BOOL __stdcall dispatch_CreateDirectoryW(LPCWSTR lpPathName, LPSECURITY_ATTRIBUTES lpSecurityAttributes)
{
    resolve_api(module_2df640, name_CreateDirectoryW, &slot_CreateDirectoryW, handle_451b7c, (ApiEntry)fallback_CreateDirectoryW);
    return slot_CreateDirectoryW(lpPathName, lpSecurityAttributes);
}

extern const char name_CreateDialogParamW[];
extern HWND (__stdcall *slot_CreateDialogParamW)(HINSTANCE, LPCWSTR, HWND, DLGPROC, LPARAM);
extern HWND __stdcall fallback_CreateDialogParamW(HINSTANCE, LPCWSTR, HWND, DLGPROC, LPARAM);
HWND __stdcall dispatch_CreateDialogParamW(HINSTANCE hInstance, LPCWSTR lpTemplateName, HWND hWndParent, DLGPROC lpDialogFunc, LPARAM dwInitParam)
{
    resolve_api(module_2df620, name_CreateDialogParamW, &slot_CreateDialogParamW, handle_451b78, (ApiEntry)fallback_CreateDialogParamW);
    return slot_CreateDialogParamW(hInstance, lpTemplateName, hWndParent, lpDialogFunc, dwInitParam);
}

extern const char name_GetSystemDirectoryW[];
extern UINT (__stdcall *slot_GetSystemDirectoryW)(LPWSTR, UINT);
extern UINT __stdcall fallback_GetSystemDirectoryW(LPWSTR, UINT);
UINT __stdcall dispatch_GetSystemDirectoryW(LPWSTR lpBuffer, UINT uSize)
{
    resolve_api(module_2df640, name_GetSystemDirectoryW, &slot_GetSystemDirectoryW, handle_451b74, (ApiEntry)fallback_GetSystemDirectoryW);
    return slot_GetSystemDirectoryW(lpBuffer, uSize);
}

extern const char name_PeekMessageW[];
extern BOOL (__stdcall *slot_PeekMessageW)(LPMSG, HWND, UINT, UINT, UINT);
extern BOOL __stdcall fallback_PeekMessageW(LPMSG, HWND, UINT, UINT, UINT);
BOOL __stdcall dispatch_PeekMessageW(LPMSG lpMsg, HWND hWnd, UINT wMsgFilterMin, UINT wMsgFilterMax, UINT wRemoveMsg)
{
    resolve_api(module_2df620, name_PeekMessageW, &slot_PeekMessageW, handle_451b70, (ApiEntry)fallback_PeekMessageW);
    return slot_PeekMessageW(lpMsg, hWnd, wMsgFilterMin, wMsgFilterMax, wRemoveMsg);
}

extern const char name_CopyFileW[];
extern BOOL (__stdcall *slot_CopyFileW)(LPCWSTR, LPCWSTR, BOOL);
extern BOOL __stdcall fallback_CopyFileW(LPCWSTR, LPCWSTR, BOOL);
BOOL __stdcall dispatch_CopyFileW(LPCWSTR lpExistingFileName, LPCWSTR lpNewFileName, BOOL bFailIfExists)
{
    resolve_api(module_2df640, name_CopyFileW, &slot_CopyFileW, handle_451b6c, (ApiEntry)fallback_CopyFileW);
    return slot_CopyFileW(lpExistingFileName, lpNewFileName, bFailIfExists);
}

extern const char name_LoadIconW[];
extern HICON (__stdcall *slot_LoadIconW)(HINSTANCE, LPCWSTR);
extern HICON __stdcall fallback_LoadIconW(HINSTANCE, LPCWSTR);
HICON __stdcall dispatch_LoadIconW(HINSTANCE hInstance, LPCWSTR lpIconName)
{
    resolve_api(module_2df620, name_LoadIconW, &slot_LoadIconW, handle_451b64, (ApiEntry)fallback_LoadIconW);
    return slot_LoadIconW(hInstance, lpIconName);
}

extern const char name_GetModuleFileNameW[];
extern DWORD (__stdcall *slot_GetModuleFileNameW)(HMODULE, LPWSTR, DWORD);
extern DWORD __stdcall fallback_GetModuleFileNameW(HMODULE, LPWSTR, DWORD);
DWORD __stdcall dispatch_GetModuleFileNameW(HMODULE hModule, LPWSTR lpFilename, DWORD nSize)
{
    resolve_api(module_2df640, name_GetModuleFileNameW, &slot_GetModuleFileNameW, handle_451b60, (ApiEntry)fallback_GetModuleFileNameW);
    return slot_GetModuleFileNameW(hModule, lpFilename, nSize);
}

extern const char name_CreateFileW[];
extern HANDLE (__stdcall *slot_CreateFileW)(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
extern HANDLE __stdcall fallback_CreateFileW(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
HANDLE __stdcall dispatch_CreateFileW(LPCWSTR lpFileName, DWORD dwDesiredAccess, DWORD dwShareMode, LPSECURITY_ATTRIBUTES lpSecurityAttributes, DWORD dwCreationDisposition, DWORD dwFlagsAndAttributes, HANDLE hTemplateFile)
{
    resolve_api(module_2df640, name_CreateFileW, &slot_CreateFileW, handle_451b5c, (ApiEntry)fallback_CreateFileW);
    return slot_CreateFileW(lpFileName, dwDesiredAccess, dwShareMode, lpSecurityAttributes, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
}

extern const char name_SetPropW[];
extern BOOL (__stdcall *slot_SetPropW)(HWND, LPCWSTR, HANDLE);
extern BOOL __stdcall fallback_SetPropW(HWND, LPCWSTR, HANDLE);
BOOL __stdcall dispatch_SetPropW(HWND hWnd, LPCWSTR lpString, HANDLE hData)
{
    resolve_api(module_2df620, name_SetPropW, &slot_SetPropW, handle_451b58, (ApiEntry)fallback_SetPropW);
    return slot_SetPropW(hWnd, lpString, hData);
}

extern const char name_GetCurrentDirectoryW[];
extern DWORD (__stdcall *slot_GetCurrentDirectoryW)(DWORD, LPWSTR);
extern DWORD __stdcall fallback_GetCurrentDirectoryW(DWORD, LPWSTR);
DWORD __stdcall dispatch_GetCurrentDirectoryW(DWORD nBufferLength, LPWSTR lpBuffer)
{
    resolve_api(module_2df640, name_GetCurrentDirectoryW, &slot_GetCurrentDirectoryW, handle_451b54, (ApiEntry)fallback_GetCurrentDirectoryW);
    return slot_GetCurrentDirectoryW(nBufferLength, lpBuffer);
}

extern const char name_MoveFileW[];
extern BOOL (__stdcall *slot_MoveFileW)(LPCWSTR, LPCWSTR);
extern BOOL __stdcall fallback_MoveFileW(LPCWSTR, LPCWSTR);
BOOL __stdcall dispatch_MoveFileW(LPCWSTR lpExistingFileName, LPCWSTR lpNewFileName)
{
    resolve_api(module_2df640, name_MoveFileW, &slot_MoveFileW, handle_451b50, (ApiEntry)fallback_MoveFileW);
    return slot_MoveFileW(lpExistingFileName, lpNewFileName);
}

extern const char name_ShellExecuteW[];
extern HINSTANCE (__stdcall *slot_ShellExecuteW)(HWND, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, INT);
extern HINSTANCE __stdcall fallback_ShellExecuteW(HWND, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, INT);
HINSTANCE __stdcall dispatch_ShellExecuteW(HWND hwnd, LPCWSTR lpOperation, LPCWSTR lpFile, LPCWSTR lpParameters, LPCWSTR lpDirectory, INT nShowCmd)
{
    resolve_api(module_2df758, name_ShellExecuteW, &slot_ShellExecuteW, handle_451b48, (ApiEntry)fallback_ShellExecuteW);
    return slot_ShellExecuteW(hwnd, lpOperation, lpFile, lpParameters, lpDirectory, nShowCmd);
}

extern const char name_SetCurrentDirectoryW[];
extern BOOL (__stdcall *slot_SetCurrentDirectoryW)(LPCWSTR);
extern BOOL __stdcall fallback_SetCurrentDirectoryW(LPCWSTR);
BOOL __stdcall dispatch_SetCurrentDirectoryW(LPCWSTR lpPathName)
{
    resolve_api(module_2df640, name_SetCurrentDirectoryW, &slot_SetCurrentDirectoryW, handle_451b44, (ApiEntry)fallback_SetCurrentDirectoryW);
    return slot_SetCurrentDirectoryW(lpPathName);
}

extern const char name_RemoveDirectoryW[];
extern BOOL (__stdcall *slot_RemoveDirectoryW)(LPCWSTR);
extern BOOL __stdcall fallback_RemoveDirectoryW(LPCWSTR);
BOOL __stdcall dispatch_RemoveDirectoryW(LPCWSTR lpPathName)
{
    resolve_api(module_2df640, name_RemoveDirectoryW, &slot_RemoveDirectoryW, handle_451b40, (ApiEntry)fallback_RemoveDirectoryW);
    return slot_RemoveDirectoryW(lpPathName);
}

extern const char name_RegisterWindowMessageW[];
extern UINT (__stdcall *slot_RegisterWindowMessageW)(LPCWSTR);
extern UINT __stdcall fallback_RegisterWindowMessageW(LPCWSTR);
UINT __stdcall dispatch_RegisterWindowMessageW(LPCWSTR lpString)
{
    resolve_api(module_2df620, name_RegisterWindowMessageW, &slot_RegisterWindowMessageW, handle_451b3c, (ApiEntry)fallback_RegisterWindowMessageW);
    return slot_RegisterWindowMessageW(lpString);
}

extern const char name_FindNextFileW[];
extern BOOL (__stdcall *slot_FindNextFileW)(HANDLE, LPWIN32_FIND_DATAW);
extern BOOL __stdcall fallback_FindNextFileW(HANDLE, LPWIN32_FIND_DATAW);
BOOL __stdcall dispatch_FindNextFileW(HANDLE hFindFile, LPWIN32_FIND_DATAW lpFindFileData)
{
    resolve_api(module_2df640, name_FindNextFileW, &slot_FindNextFileW, handle_451b38, (ApiEntry)fallback_FindNextFileW);
    return slot_FindNextFileW(hFindFile, lpFindFileData);
}

extern const char name_GetFileAttributesExW[];
extern BOOL (__stdcall *slot_GetFileAttributesExW)(LPCWSTR, GET_FILEEX_INFO_LEVELS, LPVOID);
extern BOOL __stdcall fallback_GetFileAttributesExW(LPCWSTR, GET_FILEEX_INFO_LEVELS, LPVOID);
BOOL __stdcall dispatch_GetFileAttributesExW(LPCWSTR lpFileName, GET_FILEEX_INFO_LEVELS fInfoLevelId, LPVOID lpFileInformation)
{
    resolve_api(module_2df640, name_GetFileAttributesExW, &slot_GetFileAttributesExW, handle_451b34, (ApiEntry)fallback_GetFileAttributesExW);
    return slot_GetFileAttributesExW(lpFileName, fInfoLevelId, lpFileInformation);
}

extern const char name_RemovePropW[];
extern HANDLE (__stdcall *slot_RemovePropW)(HWND, LPCWSTR);
extern HANDLE __stdcall fallback_RemovePropW(HWND, LPCWSTR);
HANDLE __stdcall dispatch_RemovePropW(HWND hWnd, LPCWSTR lpString)
{
    resolve_api(module_2df620, name_RemovePropW, &slot_RemovePropW, handle_451b30, (ApiEntry)fallback_RemovePropW);
    return slot_RemovePropW(hWnd, lpString);
}

extern const char name_GetWindowsDirectoryW[];
extern UINT (__stdcall *slot_GetWindowsDirectoryW)(LPWSTR, UINT);
extern UINT __stdcall fallback_GetWindowsDirectoryW(LPWSTR, UINT);
UINT __stdcall dispatch_GetWindowsDirectoryW(LPWSTR lpBuffer, UINT uSize)
{
    resolve_api(module_2df640, name_GetWindowsDirectoryW, &slot_GetWindowsDirectoryW, handle_451b2c, (ApiEntry)fallback_GetWindowsDirectoryW);
    return slot_GetWindowsDirectoryW(lpBuffer, uSize);
}

extern const char name_DeleteFileW[];
extern BOOL (__stdcall *slot_DeleteFileW)(LPCWSTR);
extern BOOL __stdcall fallback_DeleteFileW(LPCWSTR);
BOOL __stdcall dispatch_DeleteFileW(LPCWSTR lpFileName)
{
    resolve_api(module_2df640, name_DeleteFileW, &slot_DeleteFileW, handle_451b28, (ApiEntry)fallback_DeleteFileW);
    return slot_DeleteFileW(lpFileName);
}

extern const char name_SetFileAttributesW[];
extern BOOL (__stdcall *slot_SetFileAttributesW)(LPCWSTR, DWORD);
extern BOOL __stdcall fallback_SetFileAttributesW(LPCWSTR, DWORD);
BOOL __stdcall dispatch_SetFileAttributesW(LPCWSTR lpFileName, DWORD dwFileAttributes)
{
    resolve_api(module_2df640, name_SetFileAttributesW, &slot_SetFileAttributesW, handle_451b24, (ApiEntry)fallback_SetFileAttributesW);
    return slot_SetFileAttributesW(lpFileName, dwFileAttributes);
}

extern const char name_WideCharToMultiByte[];
extern int (__stdcall *slot_WideCharToMultiByte)(UINT, DWORD, LPCWCH, int, LPSTR, int, LPCCH, LPBOOL);
extern int __stdcall fallback_WideCharToMultiByte(UINT, DWORD, LPCWCH, int, LPSTR, int, LPCCH, LPBOOL);
int __stdcall dispatch_WideCharToMultiByte(UINT CodePage, DWORD dwFlags, LPCWCH lpWideCharStr, int cchWideChar, LPSTR lpMultiByteStr, int cbMultiByte, LPCCH lpDefaultChar, LPBOOL lpUsedDefaultChar)
{
    resolve_api(module_2df640, name_WideCharToMultiByte, &slot_WideCharToMultiByte, handle_451b1c, (ApiEntry)fallback_WideCharToMultiByte);
    return slot_WideCharToMultiByte(CodePage, dwFlags, lpWideCharStr, cchWideChar, lpMultiByteStr, cbMultiByte, lpDefaultChar, lpUsedDefaultChar);
}

extern const char name_MultiByteToWideChar[];
extern int (__stdcall *slot_MultiByteToWideChar)(UINT, DWORD, LPCCH, int, LPWSTR, int);
extern int __stdcall fallback_MultiByteToWideChar(UINT, DWORD, LPCCH, int, LPWSTR, int);
int __stdcall dispatch_MultiByteToWideChar(UINT CodePage, DWORD dwFlags, LPCCH lpMultiByteStr, int cbMultiByte, LPWSTR lpWideCharStr, int cchWideChar)
{
    resolve_api(module_2df640, name_MultiByteToWideChar, &slot_MultiByteToWideChar, handle_451b18, (ApiEntry)fallback_MultiByteToWideChar);
    return slot_MultiByteToWideChar(CodePage, dwFlags, lpMultiByteStr, cbMultiByte, lpWideCharStr, cchWideChar);
}

extern const char name_FormatMessageW[];
extern DWORD (__stdcall *slot_FormatMessageW)(DWORD, LPCVOID, DWORD, DWORD, LPWSTR, DWORD, va_list *);
extern DWORD __stdcall fallback_FormatMessageW(DWORD, LPCVOID, DWORD, DWORD, LPWSTR, DWORD, va_list *);
DWORD __stdcall dispatch_FormatMessageW(DWORD dwFlags, LPCVOID lpSource, DWORD dwMessageId, DWORD dwLanguageId, LPWSTR lpBuffer, DWORD nSize, va_list * Arguments)
{
    resolve_api(module_2df640, name_FormatMessageW, &slot_FormatMessageW, handle_451b14, (ApiEntry)fallback_FormatMessageW);
    return slot_FormatMessageW(dwFlags, lpSource, dwMessageId, dwLanguageId, lpBuffer, nSize, Arguments);
}

extern const char name_GetClipboardData[];
extern HANDLE (__stdcall *slot_GetClipboardData)(UINT);
extern HANDLE __stdcall fallback_GetClipboardData(UINT);
HANDLE __stdcall dispatch_GetClipboardData(UINT uFormat)
{
    resolve_api(module_2df620, name_GetClipboardData, &slot_GetClipboardData, handle_451b10, (ApiEntry)fallback_GetClipboardData);
    return slot_GetClipboardData(uFormat);
}

extern const char name_GetVersionExW[];
extern BOOL (__stdcall *slot_GetVersionExW)(LPOSVERSIONINFOW);
extern BOOL __stdcall fallback_GetVersionExW(LPOSVERSIONINFOW);
BOOL __stdcall dispatch_GetVersionExW(LPOSVERSIONINFOW lpVersionInformation)
{
    resolve_api(module_2df640, name_GetVersionExW, &slot_GetVersionExW, handle_451b0c, (ApiEntry)fallback_GetVersionExW);
    return slot_GetVersionExW(lpVersionInformation);
}

extern const char name_GetProcAddress[];
extern FARPROC (__stdcall *slot_GetProcAddress)(HMODULE, LPCSTR);
extern FARPROC __stdcall fallback_GetProcAddress(HMODULE, LPCSTR);
FARPROC __stdcall dispatch_GetProcAddress(HMODULE hModule, LPCSTR lpProcName)
{
    resolve_api(module_2df640, name_GetProcAddress, &slot_GetProcAddress, handle_451b04, (ApiEntry)fallback_GetProcAddress);
    return slot_GetProcAddress(hModule, lpProcName);
}

extern const char name_CreateProcessW[];
extern BOOL (__stdcall *slot_CreateProcessW)(LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW, LPPROCESS_INFORMATION);
extern BOOL __stdcall fallback_CreateProcessW(LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW, LPPROCESS_INFORMATION);
BOOL __stdcall dispatch_CreateProcessW(LPCWSTR lpApplicationName, LPWSTR lpCommandLine, LPSECURITY_ATTRIBUTES lpProcessAttributes, LPSECURITY_ATTRIBUTES lpThreadAttributes, BOOL bInheritHandles, DWORD dwCreationFlags, LPVOID lpEnvironment, LPCWSTR lpCurrentDirectory, LPSTARTUPINFOW lpStartupInfo, LPPROCESS_INFORMATION lpProcessInformation)
{
    resolve_api(module_2df640, name_CreateProcessW, &slot_CreateProcessW, handle_451b00, (ApiEntry)fallback_CreateProcessW);
    return slot_CreateProcessW(lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, dwCreationFlags, lpEnvironment, lpCurrentDirectory, lpStartupInfo, lpProcessInformation);
}

extern const char name_GetComputerNameW[];
extern BOOL (__stdcall *slot_GetComputerNameW)(LPWSTR, LPDWORD);
extern BOOL __stdcall fallback_GetComputerNameW(LPWSTR, LPDWORD);
BOOL __stdcall dispatch_GetComputerNameW(LPWSTR lpBuffer, LPDWORD nSize)
{
    resolve_api(module_2df640, name_GetComputerNameW, &slot_GetComputerNameW, handle_451afc, (ApiEntry)fallback_GetComputerNameW);
    return slot_GetComputerNameW(lpBuffer, nSize);
}

extern const char name_FindExecutableW[];
extern HINSTANCE (__stdcall *slot_FindExecutableW)(LPCWSTR, LPCWSTR, LPWSTR);
extern HINSTANCE __stdcall fallback_FindExecutableW(LPCWSTR, LPCWSTR, LPWSTR);
HINSTANCE __stdcall dispatch_FindExecutableW(LPCWSTR lpFile, LPCWSTR lpDirectory, LPWSTR lpResult)
{
    resolve_api(module_2df758, name_FindExecutableW, &slot_FindExecutableW, handle_451ae4, (ApiEntry)fallback_FindExecutableW);
    return slot_FindExecutableW(lpFile, lpDirectory, lpResult);
}

extern const char name_CreateEventW[];
extern HANDLE (__stdcall *slot_CreateEventW)(LPSECURITY_ATTRIBUTES, BOOL, BOOL, LPCWSTR);
extern HANDLE __stdcall fallback_CreateEventW(LPSECURITY_ATTRIBUTES, BOOL, BOOL, LPCWSTR);
HANDLE __stdcall dispatch_CreateEventW(LPSECURITY_ATTRIBUTES lpEventAttributes, BOOL bManualReset, BOOL bInitialState, LPCWSTR lpName)
{
    resolve_api(module_2df640, name_CreateEventW, &slot_CreateEventW, handle_451ae0, (ApiEntry)fallback_CreateEventW);
    return slot_CreateEventW(lpEventAttributes, bManualReset, bInitialState, lpName);
}

extern const char name_FindResourceW[];
extern HRSRC (__stdcall *slot_FindResourceW)(HMODULE, LPCWSTR, LPCWSTR);
extern HRSRC __stdcall fallback_FindResourceW(HMODULE, LPCWSTR, LPCWSTR);
HRSRC __stdcall dispatch_FindResourceW(HMODULE hModule, LPCWSTR lpName, LPCWSTR lpType)
{
    resolve_api(module_2df640, name_FindResourceW, &slot_FindResourceW, handle_451adc, (ApiEntry)fallback_FindResourceW);
    return slot_FindResourceW(hModule, lpName, lpType);
}

extern const char name_FindResourceExW[];
extern HRSRC (__stdcall *slot_FindResourceExW)(HMODULE, LPCWSTR, LPCWSTR, WORD);
extern HRSRC __stdcall fallback_FindResourceExW(HMODULE, LPCWSTR, LPCWSTR, WORD);
HRSRC __stdcall dispatch_FindResourceExW(HMODULE hModule, LPCWSTR lpType, LPCWSTR lpName, WORD wLanguage)
{
    resolve_api(module_2df640, name_FindResourceExW, &slot_FindResourceExW, handle_451ad8, (ApiEntry)fallback_FindResourceExW);
    return slot_FindResourceExW(hModule, lpType, lpName, wLanguage);
}

extern const char name_LoadCursorW[];
extern HCURSOR (__stdcall *slot_LoadCursorW)(HINSTANCE, LPCWSTR);
extern HCURSOR __stdcall fallback_LoadCursorW(HINSTANCE, LPCWSTR);
HCURSOR __stdcall dispatch_LoadCursorW(HINSTANCE hInstance, LPCWSTR lpCursorName)
{
    resolve_api(module_2df620, name_LoadCursorW, &slot_LoadCursorW, handle_451ad4, (ApiEntry)fallback_LoadCursorW);
    return slot_LoadCursorW(hInstance, lpCursorName);
}

extern const char name_SetWindowLongW[];
extern LONG (__stdcall *slot_SetWindowLongW)(HWND, int, LONG);
extern LONG __stdcall fallback_SetWindowLongW(HWND, int, LONG);
LONG __stdcall dispatch_SetWindowLongW(HWND hWnd, int nIndex, LONG dwNewLong)
{
    resolve_api(module_2df620, name_SetWindowLongW, &slot_SetWindowLongW, handle_451ad0, (ApiEntry)fallback_SetWindowLongW);
    return slot_SetWindowLongW(hWnd, nIndex, dwNewLong);
}

extern const char name_PostMessageW[];
extern BOOL (__stdcall *slot_PostMessageW)(HWND, UINT, WPARAM, LPARAM);
extern BOOL __stdcall fallback_PostMessageW(HWND, UINT, WPARAM, LPARAM);
BOOL __stdcall dispatch_PostMessageW(HWND hWnd, UINT Msg, WPARAM wParam, LPARAM lParam)
{
    resolve_api(module_2df620, name_PostMessageW, &slot_PostMessageW, handle_451ac8, (ApiEntry)fallback_PostMessageW);
    return slot_PostMessageW(hWnd, Msg, wParam, lParam);
}

extern const char name_GetKeyNameTextW[];
extern int (__stdcall *slot_GetKeyNameTextW)(LONG, LPWSTR, int);
extern int __stdcall fallback_GetKeyNameTextW(LONG, LPWSTR, int);
int __stdcall dispatch_GetKeyNameTextW(LONG lParam, LPWSTR lpString, int cchSize)
{
    resolve_api(module_2df620, name_GetKeyNameTextW, &slot_GetKeyNameTextW, handle_451ac4, (ApiEntry)fallback_GetKeyNameTextW);
    return slot_GetKeyNameTextW(lParam, lpString, cchSize);
}

extern const char name_MapVirtualKeyW[];
extern UINT (__stdcall *slot_MapVirtualKeyW)(UINT, UINT);
extern UINT __stdcall fallback_MapVirtualKeyW(UINT, UINT);
UINT __stdcall dispatch_MapVirtualKeyW(UINT uCode, UINT uMapType)
{
    resolve_api(module_2df620, name_MapVirtualKeyW, &slot_MapVirtualKeyW, handle_451ac0, (ApiEntry)fallback_MapVirtualKeyW);
    return slot_MapVirtualKeyW(uCode, uMapType);
}

extern const char name_DefWindowProcW[];
extern LRESULT (__stdcall *slot_DefWindowProcW)(HWND, UINT, WPARAM, LPARAM);
extern LRESULT __stdcall fallback_DefWindowProcW(HWND, UINT, WPARAM, LPARAM);
LRESULT __stdcall dispatch_DefWindowProcW(HWND hWnd, UINT Msg, WPARAM wParam, LPARAM lParam)
{
    resolve_api(module_2df620, name_DefWindowProcW, &slot_DefWindowProcW, handle_451abc, (ApiEntry)fallback_DefWindowProcW);
    return slot_DefWindowProcW(hWnd, Msg, wParam, lParam);
}

extern const char name_GetWindowLongW[];
extern LONG (__stdcall *slot_GetWindowLongW)(HWND, int);
extern LONG __stdcall fallback_GetWindowLongW(HWND, int);
LONG __stdcall dispatch_GetWindowLongW(HWND hWnd, int nIndex)
{
    resolve_api(module_2df620, name_GetWindowLongW, &slot_GetWindowLongW, handle_451ab8, (ApiEntry)fallback_GetWindowLongW);
    return slot_GetWindowLongW(hWnd, nIndex);
}

extern const char name_GlobalAddAtomW[];
extern ATOM (__stdcall *slot_GlobalAddAtomW)(LPCWSTR);
extern ATOM __stdcall fallback_GlobalAddAtomW(LPCWSTR);
ATOM __stdcall dispatch_GlobalAddAtomW(LPCWSTR lpString)
{
    resolve_api(module_2df640, name_GlobalAddAtomW, &slot_GlobalAddAtomW, handle_451ab0, (ApiEntry)fallback_GlobalAddAtomW);
    return slot_GlobalAddAtomW(lpString);
}

extern const char name_SystemParametersInfoW[];
extern BOOL (__stdcall *slot_SystemParametersInfoW)(UINT, UINT, PVOID, UINT);
extern BOOL __stdcall fallback_SystemParametersInfoW(UINT, UINT, PVOID, UINT);
BOOL __stdcall dispatch_SystemParametersInfoW(UINT uiAction, UINT uiParam, PVOID pvParam, UINT fWinIni)
{
    resolve_api(module_2df620, name_SystemParametersInfoW, &slot_SystemParametersInfoW, handle_451aac, (ApiEntry)fallback_SystemParametersInfoW);
    return slot_SystemParametersInfoW(uiAction, uiParam, pvParam, fWinIni);
}

extern const char name_EnableWindow[];
extern BOOL (__stdcall *slot_EnableWindow)(HWND, BOOL);
extern BOOL __stdcall fallback_EnableWindow(HWND, BOOL);
BOOL __stdcall dispatch_EnableWindow(HWND hWnd, BOOL bEnable)
{
    resolve_api(module_2df620, name_EnableWindow, &slot_EnableWindow, handle_451aa8, (ApiEntry)fallback_EnableWindow);
    return slot_EnableWindow(hWnd, bEnable);
}

extern const char name_CallWindowProcW[];
extern LRESULT (__stdcall *slot_CallWindowProcW)(WNDPROC, HWND, UINT, WPARAM, LPARAM);
extern LRESULT __stdcall fallback_CallWindowProcW(WNDPROC, HWND, UINT, WPARAM, LPARAM);
LRESULT __stdcall dispatch_CallWindowProcW(WNDPROC lpPrevWndFunc, HWND hWnd, UINT Msg, WPARAM wParam, LPARAM lParam)
{
    resolve_api(module_2df620, name_CallWindowProcW, &slot_CallWindowProcW, handle_451aa0, (ApiEntry)fallback_CallWindowProcW);
    return slot_CallWindowProcW(lpPrevWndFunc, hWnd, Msg, wParam, lParam);
}

extern const char name_SetMenuItemInfoW[];
extern BOOL (__stdcall *slot_SetMenuItemInfoW)(HMENU, UINT, BOOL, LPCMENUITEMINFOW);
extern BOOL __stdcall fallback_SetMenuItemInfoW(HMENU, UINT, BOOL, LPCMENUITEMINFOW);
BOOL __stdcall dispatch_SetMenuItemInfoW(HMENU hmenu, UINT item, BOOL fByPositon, LPCMENUITEMINFOW lpmii)
{
    resolve_api(module_2df620, name_SetMenuItemInfoW, &slot_SetMenuItemInfoW, handle_451a9c, (ApiEntry)fallback_SetMenuItemInfoW);
    return slot_SetMenuItemInfoW(hmenu, item, fByPositon, lpmii);
}

extern const char name_CreateWindowExW[];
extern HWND (__stdcall *slot_CreateWindowExW)(DWORD, LPCWSTR, LPCWSTR, DWORD, int, int, int, int, HWND, HMENU, HINSTANCE, LPVOID);
extern HWND __stdcall fallback_CreateWindowExW(DWORD, LPCWSTR, LPCWSTR, DWORD, int, int, int, int, HWND, HMENU, HINSTANCE, LPVOID);
HWND __stdcall dispatch_CreateWindowExW(DWORD dwExStyle, LPCWSTR lpClassName, LPCWSTR lpWindowName, DWORD dwStyle, int X, int Y, int nWidth, int nHeight, HWND hWndParent, HMENU hMenu, HINSTANCE hInstance, LPVOID lpParam)
{
    resolve_api(module_2df620, name_CreateWindowExW, &slot_CreateWindowExW, handle_451a94, (ApiEntry)fallback_CreateWindowExW);
    return slot_CreateWindowExW(dwExStyle, lpClassName, lpWindowName, dwStyle, X, Y, nWidth, nHeight, hWndParent, hMenu, hInstance, lpParam);
}

extern const char name_DefMDIChildProcW[];
extern LRESULT (__stdcall *slot_DefMDIChildProcW)(HWND, UINT, WPARAM, LPARAM);
extern LRESULT __stdcall fallback_DefMDIChildProcW(HWND, UINT, WPARAM, LPARAM);
LRESULT __stdcall dispatch_DefMDIChildProcW(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
    resolve_api(module_2df620, name_DefMDIChildProcW, &slot_DefMDIChildProcW, handle_451a90, (ApiEntry)fallback_DefMDIChildProcW);
    return slot_DefMDIChildProcW(hWnd, uMsg, wParam, lParam);
}

extern const char name_GetModuleHandleW[];
extern HMODULE (__stdcall *slot_GetModuleHandleW)(LPCWSTR);
extern HMODULE __stdcall fallback_GetModuleHandleW(LPCWSTR);
HMODULE __stdcall dispatch_GetModuleHandleW(LPCWSTR lpModuleName)
{
    resolve_api(module_2df640, name_GetModuleHandleW, &slot_GetModuleHandleW, handle_451a8c, (ApiEntry)fallback_GetModuleHandleW);
    return slot_GetModuleHandleW(lpModuleName);
}

extern const char name_LoadLibraryW[];
extern HMODULE (__stdcall *slot_LoadLibraryW)(LPCWSTR);
extern HMODULE __stdcall fallback_LoadLibraryW(LPCWSTR);
HMODULE __stdcall dispatch_LoadLibraryW(LPCWSTR lpLibFileName)
{
    resolve_api(module_2df640, name_LoadLibraryW, &slot_LoadLibraryW, handle_451a88, (ApiEntry)fallback_LoadLibraryW);
    return slot_LoadLibraryW(lpLibFileName);
}

extern const char name_LoadMenuW[];
extern HMENU (__stdcall *slot_LoadMenuW)(HINSTANCE, LPCWSTR);
extern HMENU __stdcall fallback_LoadMenuW(HINSTANCE, LPCWSTR);
HMENU __stdcall dispatch_LoadMenuW(HINSTANCE hInstance, LPCWSTR lpMenuName)
{
    resolve_api(module_2df620, name_LoadMenuW, &slot_LoadMenuW, handle_451a84, (ApiEntry)fallback_LoadMenuW);
    return slot_LoadMenuW(hInstance, lpMenuName);
}

extern const char name_RegisterClassExW[];
extern ATOM (__stdcall *slot_RegisterClassExW)(const WNDCLASSEXW *);
extern ATOM __stdcall fallback_RegisterClassExW(const WNDCLASSEXW *);
ATOM __stdcall dispatch_RegisterClassExW(const WNDCLASSEXW * arg1)
{
    resolve_api(module_2df620, name_RegisterClassExW, &slot_RegisterClassExW, handle_451a80, (ApiEntry)fallback_RegisterClassExW);
    return slot_RegisterClassExW(arg1);
}

extern const char name_GetClassInfoExW[];
extern BOOL (__stdcall *slot_GetClassInfoExW)(HINSTANCE, LPCWSTR, LPWNDCLASSEXW);
extern BOOL __stdcall fallback_GetClassInfoExW(HINSTANCE, LPCWSTR, LPWNDCLASSEXW);
BOOL __stdcall dispatch_GetClassInfoExW(HINSTANCE hInstance, LPCWSTR lpszClass, LPWNDCLASSEXW lpwcx)
{
    resolve_api(module_2df620, name_GetClassInfoExW, &slot_GetClassInfoExW, handle_451a7c, (ApiEntry)fallback_GetClassInfoExW);
    return slot_GetClassInfoExW(hInstance, lpszClass, lpwcx);
}

extern const char name_GetMenuItemInfoW[];
extern BOOL (__stdcall *slot_GetMenuItemInfoW)(HMENU, UINT, BOOL, LPMENUITEMINFOW);
extern BOOL __stdcall fallback_GetMenuItemInfoW(HMENU, UINT, BOOL, LPMENUITEMINFOW);
BOOL __stdcall dispatch_GetMenuItemInfoW(HMENU hmenu, UINT item, BOOL fByPosition, LPMENUITEMINFOW lpmii)
{
    resolve_api(module_2df620, name_GetMenuItemInfoW, &slot_GetMenuItemInfoW, handle_451a78, (ApiEntry)fallback_GetMenuItemInfoW);
    return slot_GetMenuItemInfoW(hmenu, item, fByPosition, lpmii);
}

extern const char name_DialogBoxParamW[];
extern INT_PTR (__stdcall *slot_DialogBoxParamW)(HINSTANCE, LPCWSTR, HWND, DLGPROC, LPARAM);
extern INT_PTR __stdcall fallback_DialogBoxParamW(HINSTANCE, LPCWSTR, HWND, DLGPROC, LPARAM);
INT_PTR __stdcall dispatch_DialogBoxParamW(HINSTANCE hInstance, LPCWSTR lpTemplateName, HWND hWndParent, DLGPROC lpDialogFunc, LPARAM dwInitParam)
{
    resolve_api(module_2df620, name_DialogBoxParamW, &slot_DialogBoxParamW, handle_451a74, (ApiEntry)fallback_DialogBoxParamW);
    return slot_DialogBoxParamW(hInstance, lpTemplateName, hWndParent, lpDialogFunc, dwInitParam);
}

extern const char name_GetTextExtentPoint32W[];
extern BOOL (__stdcall *slot_GetTextExtentPoint32W)(HDC, LPCWSTR, int, LPSIZE);
extern BOOL __stdcall fallback_GetTextExtentPoint32W(HDC, LPCWSTR, int, LPSIZE);
BOOL __stdcall dispatch_GetTextExtentPoint32W(HDC hdc, LPCWSTR lpString, int c, LPSIZE psizl)
{
    resolve_api(module_2df8d0, name_GetTextExtentPoint32W, &slot_GetTextExtentPoint32W, handle_451a70, (ApiEntry)fallback_GetTextExtentPoint32W);
    return slot_GetTextExtentPoint32W(hdc, lpString, c, psizl);
}

extern const char name_DrawTextExW[];
extern int (__stdcall *slot_DrawTextExW)(HDC, LPWSTR, int, LPRECT, UINT, LPDRAWTEXTPARAMS);
extern int __stdcall fallback_DrawTextExW(HDC, LPWSTR, int, LPRECT, UINT, LPDRAWTEXTPARAMS);
int __stdcall dispatch_DrawTextExW(HDC hdc, LPWSTR lpchText, int cchText, LPRECT lprc, UINT format, LPDRAWTEXTPARAMS lpdtp)
{
    resolve_api(module_2df620, name_DrawTextExW, &slot_DrawTextExW, handle_451a6c, (ApiEntry)fallback_DrawTextExW);
    return slot_DrawTextExW(hdc, lpchText, cchText, lprc, format, lpdtp);
}

extern const char name_TextOutW[];
extern BOOL (__stdcall *slot_TextOutW)(HDC, int, int, LPCWSTR, int);
extern BOOL __stdcall fallback_TextOutW(HDC, int, int, LPCWSTR, int);
BOOL __stdcall dispatch_TextOutW(HDC hdc, int x, int y, LPCWSTR lpString, int c)
{
    resolve_api(module_2df8d0, name_TextOutW, &slot_TextOutW, handle_451a68, (ApiEntry)fallback_TextOutW);
    return slot_TextOutW(hdc, x, y, lpString, c);
}
