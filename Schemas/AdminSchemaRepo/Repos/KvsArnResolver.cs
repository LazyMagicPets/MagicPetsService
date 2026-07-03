using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using System.Collections.Concurrent;

namespace AdminSchemaRepo;

/// <summary>
/// Resolves the ARN of a CloudFront KeyValueStore by NAME, via the control-plane
/// <c>ListKeyValueStores</c> API, cached in-memory for the life of the process.
///
/// This lets the apphost discover the tenant's KVS ARN ({sk}-{tk}-kvs) at runtime from
/// information already in the request (the tenancy), WITHOUT the ARN being injected via an
/// <c>lz-aws-kvsarn</c> header or env var (which nothing currently provides). Mirrors the
/// naming convention + lookup in lz's <c>AwsEdgeUpdater.ResolveKvsArnAsync</c>.
///
/// IAM: the task role must allow <c>cloudfront:ListKeyValueStores</c> (List is not
/// resource-scopable, so Resource: *). Results are cached because the ARN is stable for the
/// life of the deploy and ListKeyValueStores is a slow, throttled control-plane call.
/// </summary>
public interface IKvsArnResolver
{
    /// <summary>The ARN of the KVS named <paramref name="kvsName"/>, or null if not found.</summary>
    Task<string?> ResolveArnAsync(string kvsName, CancellationToken ct = default);
}

public sealed class KvsArnResolver : IKvsArnResolver
{
    // Process-wide cache: name -> ARN. The mapping is fixed for the life of the deploy.
    private static readonly ConcurrentDictionary<string, string> _cache = new();

    private readonly IAmazonCloudFront _cloudFront;

    public KvsArnResolver(IAmazonCloudFront cloudFront) => _cloudFront = cloudFront;

    public async Task<string?> ResolveArnAsync(string kvsName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(kvsName)) return null;
        if (_cache.TryGetValue(kvsName, out var cached)) return cached;

        var seen = new List<string>();
        string? marker = null;
        try
        {
            do
            {
                var resp = await _cloudFront
                    .ListKeyValueStoresAsync(new ListKeyValueStoresRequest { Marker = marker }, ct)
                    .ConfigureAwait(false);

                var items = resp.KeyValueStoreList?.Items;
                if (items != null)
                {
                    foreach (var k in items) seen.Add(k.Name);
                    var match = items.FirstOrDefault(k => string.Equals(k.Name, kvsName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        _cache[kvsName] = match.ARN;
                        return match.ARN;
                    }
                }
                marker = resp.KeyValueStoreList?.NextMarker;
            } while (!string.IsNullOrEmpty(marker));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KvsArnResolver] ListKeyValueStores FAILED for '{kvsName}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        Console.WriteLine($"[KvsArnResolver] NO match for '{kvsName}'. stores=[{string.Join(",", seen)}]");
        return null; // not found — caller logs + skips the sync
    }
}
