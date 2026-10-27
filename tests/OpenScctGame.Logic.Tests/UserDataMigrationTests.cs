using System;
using System.IO;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class UserDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "openscct-migration-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Folder(string name, params string[] files)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (string f in files)
        {
            File.WriteAllText(Path.Combine(dir, f), name + ":" + f);
        }

        return dir;
    }

    [Fact]
    public void TheFirstRunUnderTheNewNameCopiesThePlayersFiles()
    {
        Folder(UserDataMigration.OldFolderName, "settings.json", "settings.cfg", "svm_keys_spy.ini", "logs.txt");
        string current = Path.Combine(_root, "OpenSCCT");

        Assert.Equal(3, UserDataMigration.Run(current));
        Assert.Equal("SvmGame:settings.json", File.ReadAllText(Path.Combine(current, "settings.json")));
        Assert.False(File.Exists(Path.Combine(current, "logs.txt")));
        Assert.True(File.Exists(Path.Combine(_root, UserDataMigration.OldFolderName, "settings.json")));
    }

    [Fact]
    public void NothingIsCopiedOnceTheNewFolderHasSettings()
    {
        Folder(UserDataMigration.OldFolderName, "settings.json", "settings.cfg");
        string current = Folder("OpenSCCT", "settings.json");

        Assert.Equal(0, UserDataMigration.Run(current));
        Assert.Equal("OpenSCCT:settings.json", File.ReadAllText(Path.Combine(current, "settings.json")));
        Assert.False(File.Exists(Path.Combine(current, "settings.cfg")));
    }

    [Fact]
    public void DefaultsWrittenBeforeTheFirstSaveAreReplaced()
    {
        Folder(UserDataMigration.OldFolderName, "settings.json", "svm_keys_spy.ini");
        string current = Folder("OpenSCCT", "svm_keys_spy.ini");

        Assert.Equal(2, UserDataMigration.Run(current));
        Assert.Equal("SvmGame:svm_keys_spy.ini", File.ReadAllText(Path.Combine(current, "svm_keys_spy.ini")));
    }

    [Fact]
    public void NoOldFolderMeansNothingToDo() => Assert.Equal(0, UserDataMigration.Run(Path.Combine(_root, "OpenSCCT")));
}
