using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OpenScct.Launcher.Editor;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed partial class EditorServiceTests
{
    [Fact(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    public void VerifiedUninstallHandleBlocksReplacementAndDeletesItsOwnFile()
    {
        using var dir = new TempDir();
        string path = dir.Combine("package.txt");
        File.WriteAllText(path, "package bytes");
        using (FileStream verified = NativeOpen(path))
        {
            Assert.Throws<IOException>(() => File.Move(path, dir.Combine("renamed.txt")));
            Assert.Throws<IOException>(() => File.WriteAllText(path, "user changes"));
            Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes("package bytes")), SHA256.HashData(verified));
            NativeDelete(verified.SafeFileHandle);
        }

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(dir.Combine("renamed.txt")));
    }

    [Fact(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    public void VerifiedUninstallHandleCanRemoveReadOnlyPackageFile()
    {
        using var dir = new TempDir();
        string path = dir.Combine("package.txt");
        File.WriteAllText(path, "package bytes");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            using (FileStream verified = NativeOpen(path)) NativeDelete(verified.SafeFileHandle);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    public void UninstallHandleRejectsAParentLinkEvenWithoutPreflight()
    {
        using var dir = new TempDir();
        string target = dir.Combine("outside");
        Directory.CreateDirectory(target);
        string file = Path.Combine(target, "work.txt");
        File.WriteAllText(file, "user work");
        string link = dir.Combine("redirected");
        Directory.CreateSymbolicLink(link, target);
        try
        {
            Assert.Throws<IOException>(() => NativeOpen(Path.Combine(link, "work.txt")));
            Assert.Equal("user work", File.ReadAllText(file));
        }
        finally { Directory.Delete(link); }
    }

    private static FileStream NativeOpen(string path) =>
        (FileStream)InvokeUninstallNative("OpenUninstallFile", path, FileAccess.Read, FileShare.Read, false)!;

    private static void NativeDelete(SafeFileHandle handle) => InvokeUninstallNative("MarkForDeletion", handle);

    private static object? InvokeUninstallNative(string method, params object[] args)
    {
        try
        {
            return typeof(EditorService).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
