using System;
using System.IO;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class LinuxSetupContentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "openscct-linux-setup-" + Guid.NewGuid());

    public LinuxSetupContentTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void FreshInstallCanDownloadWithoutAnOverride()
    {
        Assert.Null(LinuxSetupContent.OverrideProblem(null));
        Assert.False(LinuxSetupContent.SharedReady(null));
    }

    [Fact]
    public void IncompleteOverrideExplainsHowToRecoverBeforeDownloading()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        string problem = Assert.IsType<string>(LinuxSetupContent.OverrideProblem(_root));
        Assert.Contains(_root, problem);
        Assert.Contains("--svm-content", problem);
        Assert.Contains("SVM_CONTENT", problem);
        Assert.False(LinuxSetupContent.SharedReady(Path.Combine(_root, "shared")));
    }

    [Fact]
    public void CompleteOverrideCanProceedButMissingAudioIsRejected()
    {
        string shared = Path.Combine(_root, "shared");
        foreach (string folder in new[] { "ui", "chars", "audio" })
            Directory.CreateDirectory(Path.Combine(shared, folder));
        File.WriteAllText(Path.Combine(shared, "classes.json"), "{}");
        Assert.True(LinuxSetupContent.SharedReady(shared));
        Assert.Null(LinuxSetupContent.OverrideProblem(_root));
        Directory.Delete(Path.Combine(shared, "audio"));
        Assert.NotNull(LinuxSetupContent.OverrideProblem(_root));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
