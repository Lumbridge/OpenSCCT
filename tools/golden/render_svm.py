"""Run a render smoke test of the Godot client with a real renderer but nothing on screen.

    python -I -B tools/golden/render_svm.py [golden|steee-render] [--game game] [--godot <console exe>]
        [--visible] [-- extra user arguments such as --svm-golden-only=courtyard]

The golden renders (`--svm-smoke=golden`) and the draw-call statistics (`--svm-smoke=steee-render`) need a GPU
renderer, which `--headless` does not have. This starts Godot on a Win32 desktop of its own (created with
CreateDesktop and never shown, so no window appears, takes the focus or steals a keypress), waits for it to
exit and prints its log. `--visible` runs it on the normal desktop instead.

Exit code: Godot's (0 when the smoke test passed).
"""
from __future__ import annotations

import argparse
import ctypes
import os
import subprocess
import sys
import tempfile
import time
from ctypes import wintypes

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
DESKTOP = 'SVMRender'
GODOT = os.path.join(REPO, 'tools', 'godot', '4.7.2-stable', 'Godot_v4.7.2-stable_mono_win64_console.exe')

DESKTOP_ALL = 0x01FF
GENERIC_ALL = 0x10000000
CREATE_UNICODE_ENVIRONMENT = 0x00000400
CREATE_NO_WINDOW = 0x08000000
SYNCHRONIZE = 0x00100000
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
STILL_ACTIVE = 259


class STARTUPINFOW(ctypes.Structure):
    _fields_ = [('cb', wintypes.DWORD), ('lpReserved', wintypes.LPWSTR), ('lpDesktop', wintypes.LPWSTR),
                ('lpTitle', wintypes.LPWSTR), ('dwX', wintypes.DWORD), ('dwY', wintypes.DWORD),
                ('dwXSize', wintypes.DWORD), ('dwYSize', wintypes.DWORD), ('dwXCountChars', wintypes.DWORD),
                ('dwYCountChars', wintypes.DWORD), ('dwFillAttribute', wintypes.DWORD), ('dwFlags', wintypes.DWORD),
                ('wShowWindow', wintypes.WORD), ('cbReserved2', wintypes.WORD), ('lpReserved2', ctypes.c_void_p),
                ('hStdInput', wintypes.HANDLE), ('hStdOutput', wintypes.HANDLE), ('hStdError', wintypes.HANDLE)]


class PROCESS_INFORMATION(ctypes.Structure):
    _fields_ = [('hProcess', wintypes.HANDLE), ('hThread', wintypes.HANDLE),
                ('dwProcessId', wintypes.DWORD), ('dwThreadId', wintypes.DWORD)]


def run_hidden(command, cwd, timeout):
    """CreateProcess with lpDesktop set to a desktop of our own; returns (exit code, seconds)."""
    user32 = ctypes.WinDLL('user32', use_last_error=True)
    kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
    user32.CreateDesktopW.restype = wintypes.HANDLE
    user32.CreateDesktopW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR, ctypes.c_void_p, wintypes.DWORD,
                                      wintypes.DWORD, ctypes.c_void_p]
    user32.CloseDesktop.argtypes = [wintypes.HANDLE]
    kernel32.CreateProcessW.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, ctypes.c_void_p, ctypes.c_void_p,
                                        wintypes.BOOL, wintypes.DWORD, ctypes.c_void_p, wintypes.LPCWSTR,
                                        ctypes.POINTER(STARTUPINFOW), ctypes.POINTER(PROCESS_INFORMATION)]
    kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    kernel32.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]

    desktop = user32.CreateDesktopW(DESKTOP, None, None, 0, DESKTOP_ALL | GENERIC_ALL, None)
    if not desktop:
        raise SystemExit('CreateDesktop failed (error %d)' % ctypes.get_last_error())
    try:
        info = STARTUPINFOW()
        info.cb = ctypes.sizeof(info)
        info.lpDesktop = 'WinSta0\\' + DESKTOP
        process = PROCESS_INFORMATION()
        line = ctypes.create_unicode_buffer(subprocess.list2cmdline(command))
        started = time.time()
        if not kernel32.CreateProcessW(command[0], line, None, None, False, CREATE_NO_WINDOW, None, cwd,
                                       ctypes.byref(info), ctypes.byref(process)):
            raise SystemExit('CreateProcess failed (error %d)' % ctypes.get_last_error())
        kernel32.CloseHandle(process.hThread)
        try:
            if kernel32.WaitForSingleObject(process.hProcess, int(timeout * 1000)) != 0:
                kernel32.TerminateProcess(process.hProcess, 1)
                kernel32.WaitForSingleObject(process.hProcess, 10000)
                print('[render] killed after %ds' % timeout, file=sys.stderr)
            code = wintypes.DWORD()
            kernel32.GetExitCodeProcess(process.hProcess, ctypes.byref(code))
            return code.value, time.time() - started
        finally:
            kernel32.CloseHandle(process.hProcess)
    finally:
        user32.CloseDesktop(desktop)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('test', nargs='?', default='golden')
    parser.add_argument('--game', default=os.path.join(REPO, 'game'), help='the Godot project folder')
    parser.add_argument('--godot', default=GODOT)
    parser.add_argument('--timeout', type=float, default=600.0)
    parser.add_argument('--visible', action='store_true', help='run on the normal desktop')
    argv = sys.argv[1:]
    cut = argv.index('--') if '--' in argv else len(argv)
    args = parser.parse_args(argv[:cut])
    extra = argv[cut + 1:]
    log = os.path.join(tempfile.gettempdir(), 'svm-render-%d.log' % os.getpid())
    command = [args.godot, '--path', os.path.abspath(args.game), '--log-file', log, '--resolution', '640x360',
               '--', '--svm-smoke=' + args.test, '--svm-smoke-timeout=%d' % int(args.timeout - 30)] + extra
    if args.visible:
        started = time.time()
        code = subprocess.run(command, cwd=REPO, timeout=args.timeout).returncode
        seconds = time.time() - started
    else:
        code, seconds = run_hidden(command, REPO, args.timeout)
    try:
        with open(log, encoding='utf-8', errors='replace') as f:
            lines = f.read().splitlines()
        for line in lines:
            if line.startswith('SVM_') or 'ERROR' in line:
                print(line)
        if code != 0:
            print('[render] last lines of the log:')
            for line in lines[-25:]:
                print('    ' + line)
        os.remove(log)
    except OSError:
        print('[render] no log at %s' % log, file=sys.stderr)
    print('[render] exit %d after %.1fs' % (code, seconds))
    sys.exit(code)


if __name__ == '__main__':
    main()
