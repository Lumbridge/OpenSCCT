"""Read-only access to another process's memory (Windows).

The handle is opened with PROCESS_VM_READ and PROCESS_QUERY_INFORMATION only, so nothing in
this module can write to, inject into or suspend the target. A 64-bit Python reads a 32-bit
process fine: every address is below 4 GB.

`Memory` is the interface the reflection walker uses (read, u32, ...), so tests can hand it a
synthetic image instead of a live process (`BytesMemory`).
"""
from __future__ import annotations

import ctypes
import os
import struct
import sys
from ctypes import wintypes
from typing import Dict, List, Optional, Tuple

PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
MEM_COMMIT = 0x1000
MEM_PRIVATE = 0x20000
MEM_IMAGE = 0x1000000
PAGE_GUARD = 0x100
READABLE = {0x02, 0x04, 0x08, 0x20, 0x40, 0x80}


class MEMORY_BASIC_INFORMATION(ctypes.Structure):
    _fields_ = [('BaseAddress', ctypes.c_void_p), ('AllocationBase', ctypes.c_void_p),
                ('AllocationProtect', wintypes.DWORD), ('PartitionId', wintypes.WORD),
                ('RegionSize', ctypes.c_size_t), ('State', wintypes.DWORD), ('Protect', wintypes.DWORD),
                ('Type', wintypes.DWORD)]


if sys.platform == 'win32':
    _k32 = ctypes.WinDLL('kernel32', use_last_error=True)
    _k32.OpenProcess.restype = wintypes.HANDLE
    _k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    _k32.CloseHandle.argtypes = [wintypes.HANDLE]
    _k32.ReadProcessMemory.restype = wintypes.BOOL
    _k32.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                       ctypes.POINTER(ctypes.c_size_t)]
    _k32.VirtualQueryEx.restype = ctypes.c_size_t
    _k32.VirtualQueryEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.POINTER(MEMORY_BASIC_INFORMATION),
                                    ctypes.c_size_t]
    _k32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR,
                                                ctypes.POINTER(wintypes.DWORD)]
    _psapi = ctypes.WinDLL('psapi', use_last_error=True)
else:   # the synthetic-memory tests still run elsewhere
    _k32 = _psapi = None


class Memory:
    """Typed reads over `read(address, size) -> bytes` (short or empty when unreadable)."""

    def read(self, address: int, size: int) -> bytes:
        raise NotImplementedError

    def u8(self, a: int) -> Optional[int]:
        b = self.read(a, 1)
        return b[0] if len(b) == 1 else None

    def u16(self, a: int) -> Optional[int]:
        b = self.read(a, 2)
        return struct.unpack('<H', b)[0] if len(b) == 2 else None

    def u32(self, a: int) -> Optional[int]:
        b = self.read(a, 4)
        return struct.unpack('<I', b)[0] if len(b) == 4 else None

    def i32(self, a: int) -> Optional[int]:
        b = self.read(a, 4)
        return struct.unpack('<i', b)[0] if len(b) == 4 else None

    def f32(self, a: int) -> Optional[float]:
        b = self.read(a, 4)
        return struct.unpack('<f', b)[0] if len(b) == 4 else None

    def u64(self, a: int) -> Optional[int]:
        b = self.read(a, 8)
        return struct.unpack('<Q', b)[0] if len(b) == 8 else None

    def cstr(self, a: int, limit: int = 1024) -> Optional[str]:
        """A NUL-terminated 8-bit string, or None when unreadable or not terminated."""
        b = self.read(a, limit)
        end = b.find(b'\0')
        return b[:end].decode('latin-1') if end >= 0 else None

    def regions(self) -> List[Tuple[int, int, int, int]]:
        return []


class BytesMemory(Memory):
    """A synthetic address space: {base: bytes}. For tests."""

    def __init__(self, blocks: Dict[int, bytes]):
        self.blocks = sorted(blocks.items())

    def read(self, address: int, size: int) -> bytes:
        for base, data in self.blocks:
            if base <= address < base + len(data):
                return data[address - base:address - base + size]
        return b''

    def regions(self):
        return [(base, len(data), 0x04, MEM_PRIVATE) for base, data in self.blocks]


class Process(Memory):
    """A live process, opened for reading only."""

    def __init__(self, pid: int):
        if _k32 is None:
            raise OSError('reading another process needs Windows')
        self.pid = pid
        self.h = _k32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, pid)
        if not self.h:
            raise OSError(ctypes.get_last_error(), 'OpenProcess(%d) for reading failed' % pid)
        self.calls = 0

    def close(self):
        if self.h:
            _k32.CloseHandle(self.h)
            self.h = None

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()

    def read(self, address: int, size: int) -> bytes:
        if size <= 0 or address <= 0 or address + size > 0x1_0000_0000:
            return b''
        buf = ctypes.create_string_buffer(size)
        done = ctypes.c_size_t(0)
        self.calls += 1
        if not _k32.ReadProcessMemory(self.h, address, buf, size, ctypes.byref(done)):
            if done.value:
                return buf.raw[:done.value]
            return b''
        return buf.raw[:done.value]

    def regions(self) -> List[Tuple[int, int, int, int]]:
        """(base, size, protect, type) of committed, readable regions below 4 GB."""
        out = []
        address = 0
        mbi = MEMORY_BASIC_INFORMATION()
        while address < 0x1_0000_0000:
            if not _k32.VirtualQueryEx(self.h, address, ctypes.byref(mbi), ctypes.sizeof(mbi)):
                break
            base = mbi.BaseAddress or 0
            size = mbi.RegionSize
            if size == 0:
                break
            if mbi.State == MEM_COMMIT and (mbi.Protect & 0xFF) in READABLE and not mbi.Protect & PAGE_GUARD:
                out.append((base, size, mbi.Protect, mbi.Type))
            address = base + size
        return out

    def modules(self) -> List[Tuple[int, int, str]]:
        """(base, size, file name) of every module loaded in the process (32-bit ones included)."""
        class MODULEINFO(ctypes.Structure):
            _fields_ = [('lpBaseOfDll', ctypes.c_void_p), ('SizeOfImage', wintypes.DWORD), ('EntryPoint', ctypes.c_void_p)]
        _psapi.EnumProcessModulesEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD,
                                                ctypes.POINTER(wintypes.DWORD), wintypes.DWORD]
        _psapi.GetModuleFileNameExW.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.LPWSTR, wintypes.DWORD]
        _psapi.GetModuleInformation.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.POINTER(MODULEINFO), wintypes.DWORD]
        count = 1024
        handles = (ctypes.c_void_p * count)()
        need = wintypes.DWORD()
        if not _psapi.EnumProcessModulesEx(self.h, handles, ctypes.sizeof(handles), ctypes.byref(need), 0x03):
            return []
        out = []
        for i in range(min(count, need.value // ctypes.sizeof(ctypes.c_void_p))):
            info = MODULEINFO()
            if not _psapi.GetModuleInformation(self.h, handles[i], ctypes.byref(info), ctypes.sizeof(info)):
                continue
            name = ctypes.create_unicode_buffer(1024)
            _psapi.GetModuleFileNameExW(self.h, handles[i], name, 1024)
            out.append((info.lpBaseOfDll or 0, info.SizeOfImage, os.path.basename(name.value)))
        return sorted(out)

    def image_path(self) -> Optional[str]:
        h = _k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, self.pid)
        if not h:
            return None
        try:
            size = wintypes.DWORD(1024)
            buf = ctypes.create_unicode_buffer(size.value)
            return buf.value if _k32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(size)) else None
        finally:
            _k32.CloseHandle(h)


def pe_image(memory: Memory, base: int) -> Optional[dict]:
    """ImageBase, SizeOfImage and TimeDateStamp of a PE image mapped at `base`, read from memory."""
    if memory.read(base, 2) != b'MZ':
        return None
    pe = memory.u32(base + 0x3C)
    if pe is None or memory.read(base + pe, 4) != b'PE\0\0':
        return None
    stamp = memory.u32(base + pe + 8)
    opt = base + pe + 24
    return {'imageBase': memory.u32(opt + 28), 'sizeOfImage': memory.u32(opt + 56), 'timeDateStamp': stamp}


def process_image(pid: int) -> Optional[str]:
    if _k32 is None:
        return None
    h = _k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not h:
        return None
    try:
        size = wintypes.DWORD(1024)
        buf = ctypes.create_unicode_buffer(size.value)
        return buf.value if _k32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(size)) else None
    finally:
        _k32.CloseHandle(h)


def pids() -> List[int]:
    count = 4096
    while True:
        arr = (wintypes.DWORD * count)()
        need = wintypes.DWORD()
        if not _psapi.EnumProcesses(arr, ctypes.sizeof(arr), ctypes.byref(need)):
            return []
        if need.value < ctypes.sizeof(arr):
            return [p for p in arr[:need.value // 4] if p]
        count *= 2


def find_processes(image_name: str, under: Optional[str] = None) -> List[Tuple[int, str]]:
    """(pid, image path) of processes whose image file is named `image_name` (any case),
    optionally only those whose image lies under the folder `under`."""
    want = image_name.lower()
    root = os.path.normcase(os.path.abspath(under)) + os.sep if under else None
    found = []
    for pid in pids():
        path = process_image(pid)
        if not path or os.path.basename(path).lower() != want:
            continue
        if root and not os.path.normcase(path).startswith(root):
            continue
        found.append((pid, path))
    return found
