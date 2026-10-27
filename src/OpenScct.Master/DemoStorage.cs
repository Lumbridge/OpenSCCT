using System;
using System.Globalization;
using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace OpenScct.Master;

/// <summary>
/// Signs short-lived S3 links for round demos (docs/ONLINE.md, "Demos"). Signing is local: the master never sends
/// or receives the files, and the game servers and players never hold AWS keys.
/// </summary>
internal sealed class DemoStorage : IDisposable
{
    /// <summary>How long a server's upload link works.</summary>
    public static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(15);

    /// <summary>How long a player's download link works.</summary>
    public static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The Content-Type signed into upload links; the PUT must send the same.</summary>
    public const string ContentType = "application/octet-stream";

    private readonly AmazonS3Client _s3;
    private readonly TimeProvider _time;

    public DemoStorage(DemoOptions options, TimeProvider time)
    {
        Options = options;
        _time = time;
        _s3 = new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region) });
    }

    public DemoOptions Options { get; }

    /// <summary>A new object key: <c>demos/{yyyy-MM-dd}/{matchId}-{random}.scctdemo</c>.</summary>
    public string NewKey(long matchId) =>
        string.Create(CultureInfo.InvariantCulture,
            $"demos/{_time.GetUtcNow():yyyy-MM-dd}/{matchId}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.scctdemo");

    /// <summary>A PUT link for <paramref name="key"/> with the length signed in (S3 refuses any other size).</summary>
    public (string Url, DateTimeOffset Expires) UploadUrl(string key, long size)
    {
        DateTimeOffset expires = _time.GetUtcNow() + UploadLifetime;
        var request = new GetPreSignedUrlRequest
        {
            BucketName = Options.Bucket, Key = key, Verb = HttpVerb.PUT, Expires = expires.UtcDateTime, ContentType = ContentType,
            Protocol = Protocol.HTTPS,
        };
        request.Headers.ContentLength = size;
        return (_s3.GetPreSignedURL(request), expires);
    }

    /// <summary>A GET link for <paramref name="key"/>.</summary>
    public (string Url, DateTimeOffset Expires) DownloadUrl(string key)
    {
        DateTimeOffset expires = _time.GetUtcNow() + DownloadLifetime;
        var request = new GetPreSignedUrlRequest
        {
            BucketName = Options.Bucket, Key = key, Verb = HttpVerb.GET, Expires = expires.UtcDateTime, Protocol = Protocol.HTTPS,
        };
        return (_s3.GetPreSignedURL(request), expires);
    }

    public void Dispose() => _s3.Dispose();
}
