using Amazon.Runtime;

namespace ChatSchemaRepo;

/// <summary>
/// Caches AWS credentials configured via AddDefaultAWSOptions for use by services
/// that need to make raw AWS API calls (not using AWS SDK service clients).
/// This wrapper allows optional credentials (null in Lambda, provided in LocalWebService).
/// </summary>
public class AwsCredentialsCache
{
    private readonly Lazy<AWSCredentials> _credentials;

    public AwsCredentialsCache(AWSCredentials? credentials = null)
    {
        _credentials = new Lazy<AWSCredentials>(() =>
        {
            // Use injected credentials if available (from LocalWebService Startup)
            if (credentials != null)
                return credentials;

            // Fall back to default credential chain for Lambda (IAM role, environment vars, etc.)
            #pragma warning disable CS0618 // FallbackCredentialsFactory is deprecated but replacement isn't widely available
            return FallbackCredentialsFactory.GetCredentials();
            #pragma warning restore CS0618
        });
    }

    public AWSCredentials GetCredentials() => _credentials.Value;
}
