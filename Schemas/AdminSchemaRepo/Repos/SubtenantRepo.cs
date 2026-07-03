namespace AdminSchemaRepo;
using Amazon.CloudFront;

using Amazon.CloudFrontKeyValueStore;
using Amazon.CloudFrontKeyValueStore.Model;
using Amazon.CloudFront.Model;


public interface ISubtenantRepo : IDocumentRepo<Subtenant>
{
    Task<IActionResult> SeedPetsAsync(ICallerInfo callerinfo, string store, int numPets);
}

public class SubtenantRepo : DYDBRepository<Subtenant>, ISubtenantRepo
{
    public SubtenantRepo(
        IAmazonDynamoDB client,
        IAmazonCloudFrontKeyValueStore cloudFrontKvs,
        IAmazonCloudFront cloudFront,
        IKvsArnResolver kvsArnResolver
        ) : base(client)
    {
        _cloudFront = cloudFront;
        _cloudFrontKeyValueStore = cloudFrontKvs;
        _kvsArnResolver = kvsArnResolver;
    }

    private readonly IAmazonCloudFrontKeyValueStore _cloudFrontKeyValueStore;
    private readonly IAmazonCloudFront _cloudFront;
    private readonly IKvsArnResolver _kvsArnResolver;

    protected override void ConstructorExtensions()
    {
        // Users are stored in the TenantDB
        tableLevel = TableLevel.Tenant; // Use the TenantDB passed in callerInfo
        debug = false; // Log all calls to the console
        base.ConstructorExtensions();
    }

    public async Task<IActionResult> SeedPetsAsync(ICallerInfo callerinfo, string store, int numPets)
    {
        await Task.Delay(0);
        return new ObjectResult($"Pets seeded.")
        {
            StatusCode = 200
        };
    }

    public override async Task<ObjectResult> ListAsync(ICallerInfo callerInfo, int limit = 0)
    {  
        // We update the Subtenant records from the kvs entries each time we list
        // them. This is a mid-term solution as we start moving subtenant
        // creation from the PowerShell cmd creation model to subtenants managed
        // by this repo.
        Console.WriteLine("SubtenantRepo.ListAsync");


        // Resolve the tenant's KVS ARN by NAME ({sk}-{tk}-kvs == callerInfo.Tenant + "-kvs") via the
        // control-plane resolver. Previously this block required an `lz-aws-kvsarn` header, but
        // nothing injects one — so the sync never ran and Subtenant records were never created.
        var kvsArn = string.IsNullOrEmpty(callerInfo.Tenant)
            ? null
            : await _kvsArnResolver.ResolveArnAsync($"{callerInfo.Tenant}-kvs");

        // lz-tenantid (the request host) — look up case-INSENSITIVELY. callerInfo.Headers is a
        // case-sensitive dictionary and the header arrives title-cased, so the old exact-lowercase
        // lookup missed and the sync never ran. (AddConfigAsync works because ASP.NET's request.Headers
        // is case-insensitive.) The KVS is per-tenant, so the domain scope is belt-and-suspenders; the
        // real filter is "skip the non-tenancy entries" ({host}-auth + AuthConfigs aren't TenancyConfigs).
        var tenantHost = callerInfo.Headers?
            .FirstOrDefault(h => string.Equals(h.Key, "lz-tenantid", StringComparison.OrdinalIgnoreCase)).Value;

        if (!string.IsNullOrEmpty(kvsArn))
        {
            var tenantKvsKey = !string.IsNullOrEmpty(tenantHost) && tenantHost.Contains('.')
                ? string.Join(".", tenantHost.Split('.')[^2..])
                : null;

            var request = new ListKeysRequest
            {
                KvsARN = kvsArn,
                MaxResults = 50,
                NextToken = null
            };

            do
            {
                var response = await _cloudFrontKeyValueStore!.ListKeysAsync(request);
                request.NextToken = response.NextToken;
                foreach (var entry in response.Items)
                {
                    var entryKey = entry.Key;
                    // Only the host-keyed entries are TenancyConfigPacked; {host}-auth + AuthConfigs are not.
                    if (entryKey.EndsWith("-auth") || entryKey == "AuthConfigs") continue;
                    if (tenantKvsKey != null && !entryKey.EndsWith(tenantKvsKey)) continue;
                    var value = entry.Value;

                    // Update the Subtenant record
                    var subtenant = new Subtenant(value, entryKey);
                    try
                    {
                        try
                        {
                            await CreateAsync(callerInfo, subtenant);
                        } catch
                        {
                            await UpdateAsync(callerInfo, subtenant);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error updating subtenant {subtenant.TenantKey}: {e.Message}");
                    }
                }
            } while (request.NextToken != null);
        }
        return await base.ListAsync(callerInfo);
    }
}
