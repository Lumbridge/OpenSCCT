using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace OpenScct.Launcher.Editor;

public sealed partial class EditorService
{
    /// <summary>Opens a Windows file for verification and conditional deletion through the same handle. Package
    /// files omit delete sharing, so renaming or replacing a path while it is checked is denied.</summary>
    private static FileStream OpenUninstallFile(string path, FileAccess access, FileShare sharing, bool create = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Editor uninstall is supported on Windows only.");

        const uint Delete = 0x00010000, WriteAttributes = 0x00000100;
        const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
        const uint OpenExisting = 3, OpenAlways = 4;
        const uint Overlapped = 0x40000000, OpenReparsePoint = 0x00200000, SequentialScan = 0x08000000;
        uint desired = Delete | WriteAttributes;
        if ((access & FileAccess.Read) != 0) desired |= GenericRead;
        if ((access & FileAccess.Write) != 0) desired |= GenericWrite;
        SafeFileHandle handle = CreateUninstallFile(path, desired, (uint)sharing, IntPtr.Zero,
            create ? OpenAlways : OpenExisting, Overlapped | OpenReparsePoint | SequentialScan, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw NativeFileError("Could not open editor file", path, error);
        }

        try
        {
            FileAttributes attributes = UninstallFileAttributes(handle);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("The editor file is a directory or symbolic link; it was preserved: " + path);

            // OPEN_REPARSE_POINT protects the leaf. This also catches a parent junction substituted after the path check.
            var resolved = new StringBuilder(512);
            uint length = GetUninstallFilePath(handle, resolved, (uint)resolved.Capacity, 0);
            if (length >= resolved.Capacity)
            {
                resolved.EnsureCapacity(checked((int)length + 1));
                length = GetUninstallFilePath(handle, resolved, (uint)resolved.Capacity, 0);
            }
            if (length == 0 || length >= resolved.Capacity)
                throw NativeFileError("Could not verify editor file location", path, Marshal.GetLastPInvokeError());
            if (!string.Equals(UninstallPath(resolved.ToString()), UninstallPath(path), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The editor file resolves through a redirected path; it was preserved: " + path);

            return new FileStream(handle, access, 65536, isAsync: true);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Deletes the verified object on handle close, without resolving its path again. Read-only package
    /// files have that attribute cleared through the same handle after their bytes have matched.</summary>
    private static void MarkForDeletion(SafeFileHandle handle)
    {
        FileAttributes attributes = UninstallFileAttributes(handle);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            attributes &= ~FileAttributes.ReadOnly;
            var basic = new UninstallBasicInfo { Attributes = (uint)(attributes == 0 ? FileAttributes.Normal : attributes) };
            if (!SetUninstallBasicInfo(handle, 0, ref basic, (uint)Marshal.SizeOf<UninstallBasicInfo>()))
                throw NativeFileError("Could not clear editor file's read-only attribute", null, Marshal.GetLastPInvokeError());
        }

        var disposition = new UninstallDispositionInfo { Delete = 1 };
        if (!SetUninstallDisposition(handle, 4, ref disposition, (uint)Marshal.SizeOf<UninstallDispositionInfo>()))
            throw NativeFileError("Could not delete verified editor file", null, Marshal.GetLastPInvokeError());
    }

    private static FileAttributes UninstallFileAttributes(SafeFileHandle handle)
    {
        if (!GetUninstallAttributeTag(handle, 9, out UninstallAttributeTagInfo info, (uint)Marshal.SizeOf<UninstallAttributeTagInfo>()))
            throw NativeFileError("Could not inspect editor file", null, Marshal.GetLastPInvokeError());
        return (FileAttributes)info.Attributes;
    }

    private static string UninstallPath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        return Path.GetFullPath(path);
    }

    private static IOException NativeFileError(string action, string? path, int error)
    {
        var cause = new Win32Exception(error);
        return new IOException(action + (path is null ? "" : ": " + path) + ". " + cause.Message, cause);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UninstallAttributeTagInfo
    {
        public uint Attributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UninstallBasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UninstallDispositionInfo
    {
        public byte Delete;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateUninstallFile(string name, uint access, uint sharing, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUninstallAttributeTag(SafeFileHandle handle, int infoClass, out UninstallAttributeTagInfo info, uint size);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetUninstallFilePath(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetUninstallBasicInfo(SafeFileHandle handle, int infoClass, ref UninstallBasicInfo info, uint size);

    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetUninstallDisposition(SafeFileHandle handle, int infoClass, ref UninstallDispositionInfo info, uint size);
}
