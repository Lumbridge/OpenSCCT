using System;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using OpenScct.Maps;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class NetworkDiagnosticsTests
{
    [Fact]
    public void TlsFailureIncludesTheCertificateReasonBehindTheHttpWrapper()
    {
        var tls = new AuthenticationException("The remote certificate is invalid: UntrustedRoot");
        var http = new HttpRequestException("The SSL connection could not be established, see inner exception.", tls);
        var download = new IOException("The map list could not be loaded: " + http.Message, http);
        string description = NetworkDiagnostics.Describe(new AggregateException(download));
        Assert.Contains("map list could not be loaded", description);
        Assert.Contains("UntrustedRoot", description);
        Assert.Equal(description.IndexOf(http.Message, StringComparison.Ordinal), description.LastIndexOf(http.Message, StringComparison.Ordinal));
    }
}
