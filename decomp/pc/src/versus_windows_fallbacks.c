#include "windows_dispatch_types.h"

__declspec(dllimport) void __stdcall SetLastError(DWORD error);

LRESULT __stdcall unsupported_DispatchMessageW(const MSG * unused1)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (LRESULT)0;
}

HANDLE __stdcall unsupported_CreateMutexW(LPSECURITY_ATTRIBUTES unused1, BOOL unused2, LPCWSTR unused3)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (HANDLE)-1;
}

HANDLE __stdcall unsupported_FindFirstFileW(LPCWSTR unused1, LPWIN32_FIND_DATAW unused2)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (HANDLE)-1;
}

LRESULT __stdcall unsupported_SendMessageW(HWND unused1, UINT unused2, WPARAM unused3, LPARAM unused4)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (LRESULT)0;
}

BOOL __stdcall unsupported_CreateDirectoryW(LPCWSTR unused1, LPSECURITY_ATTRIBUTES unused2)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (BOOL)0;
}

HWND __stdcall unsupported_CreateDialogParamW(HINSTANCE unused1, LPCWSTR unused2, HWND unused3, DLGPROC unused4, LPARAM unused5)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (HWND)0;
}

BOOL __stdcall unsupported_CopyFileW(LPCWSTR unused1, LPCWSTR unused2, BOOL unused3)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (BOOL)0;
}

HANDLE __stdcall unsupported_CreateFileW(LPCWSTR unused1, DWORD unused2, DWORD unused3, LPSECURITY_ATTRIBUTES unused4, DWORD unused5, DWORD unused6, HANDLE unused7)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (HANDLE)-1;
}

HINSTANCE __stdcall unsupported_ShellExecuteW(HWND unused1, LPCWSTR unused2, LPCWSTR unused3, LPCWSTR unused4, LPCWSTR unused5, INT unused6)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (HINSTANCE)0;
}

DWORD __stdcall unsupported_FormatMessageW(DWORD unused1, LPCVOID unused2, DWORD unused3, DWORD unused4, LPWSTR unused5, DWORD unused6, va_list * unused7)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (DWORD)0;
}

BOOL __stdcall unsupported_CreateProcessW(LPCWSTR unused1, LPWSTR unused2, LPSECURITY_ATTRIBUTES unused3, LPSECURITY_ATTRIBUTES unused4, BOOL unused5, DWORD unused6, LPVOID unused7, LPCWSTR unused8, LPSTARTUPINFOW unused9, LPPROCESS_INFORMATION unused10)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (BOOL)0;
}

HANDLE __stdcall unsupported_CreateEventW(LPSECURITY_ATTRIBUTES unused1, BOOL unused2, BOOL unused3, LPCWSTR unused4)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (HANDLE)-1;
}

ATOM __stdcall unsupported_GlobalAddAtomW(LPCWSTR unused1)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (ATOM)0;
}

HWND __stdcall unsupported_CreateWindowExW(DWORD unused1, LPCWSTR unused2, LPCWSTR unused3, DWORD unused4, int unused5, int unused6, int unused7, int unused8, HWND unused9, HMENU unused10, HINSTANCE unused11, LPVOID unused12)
{
    SetLastError(120); /* ERROR_CALL_NOT_IMPLEMENTED */
    return (HWND)0;
}
