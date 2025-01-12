# This script deploys assets for a tenant, and the tenant's subtenants
# The tenantKey argument must match a tenant defined in the SystemConfig.yaml file.
# The tenant.json file, generated by Generate-kvsentires.ps1, must be present in the 
# Generated folder and must be up to date.
# This process checks for and creates these tenant assets:
# - S3 buckets for assets
# - S3 buckets for webapps 
# - DynamoDB tables 
# - CloudFront KeyValueStore entries

param( 
    [Parameter(Mandatory=$true)]
    [string]$TenantKey,
    [switch]$ReportOnly
)
Write-Host "TenantKey $TenantKey ReportOnly: $ReportOnly"

# Setup
# Don't turn on verbose here, do it below the imports if you need it
$VerbosePreference = "SilentlyContinue" # don't show Write-Verbose messages

. ./Functions/Get-SystemConfig.ps1
. ./Functions/Get-StackOutputs.ps1  
. ./Functions/Get-AssetNames.ps1  
. ./Functions/Create-S3Bucket.ps1
. ./Functions/Create-DynamoDbTable.ps1  
. ./Functions/Update-KVSEntry.ps1


# Set to Continue to debug script  
$VerbosePreference = "SilentlyContinue"
Write-Verbose "Deploying tenant assets for tenant $TenantKey"

$SystemConfig = Get-SystemConfig 
$config = $SystemConfig.Config
$Profile = $SystemConfig.Profile
$Region = $SystemConfig.Region
$Account = $SystemConfig.Account
$SystemKey = $config.SystemKey

Write-Verbose "SystemKey: $SystemKey Profile: $Profile Region: $Region Account: $Account"

# Check that the supplied tenantKey is in the SystemConfig.yaml and grab the tenant properties
if($config.Tenants.ContainsKey($TenantKey) -eq $false) {
    Write-Host "The tenant key $TenantKey is not defined in the SystemConfig.yaml file."
    exit 1
}
$Tenant = $config.Tenants[$TenantKey]


# Read the [tenant].json file. Remember this contains both the Tenant and Subtenant(s) information
$KvsEntries = Get-Content -Path ("./Generated/" + $TenantKey + ".json") -Raw | ConvertFrom-Json -Depth 10

# Get system stack outputs so we can grab the KVS arn
$ServiceStackOutputDict = Get-StackOutputs ($config.SystemKey + "---system")
$KvsARN  = $ServiceStackOutputDict["KeyValueStoreArn"]


$KvsEntries.PSObject.Properties | ForEach-Object {
    Write-Verbose "Processing $($_.Name)"
    $value = $_.Value

    # Create the s3 buckets 
    Write-Verbose "Creating S3 buckets"
    $AssetNames = Get-AssetNames -myTenant $value.kvsentry -myLevel $value.Level -report $ReportOnly -s3only $true
    foreach($assetName in $AssetNames) {
        if($ReportOnly) {
            Write-Host "   $assetName"
        } else { 
            Write-Verbose "   $assetName"
            $response = Create-S3Bucket -BucketName $assetName -Region $Region -Account $Account
        }
    }

    # Create the DynamoDB tables 
    $tableName = $config.SystemKey + "_" + $TenantKey
    if($value.Level -eq 2) {
        $tableName += "_" + $value.kvsentry.subtenantKey
    }
    if($ReportOnly) {
        Write-Host "Creating DynamoDB table $tableName"
    } else {
        Write-Verbose "Creating DynamoDB table $tableName"
        $response = Create-DynamoDbTable -TableName $tableName
    }

    # Create the KVS entries
    Write-Verbose "Creating KVS entry for: $($value.Domain)"
    $kvsentry = $value.KvsEntry | ConvertTo-Json -Depth 10 -Compress
    Write-Verbose ("Entry Length: " + $kvsentry.Length)
    $kvskey = $value.Domain
    if($ReportOnly) {
        Write-Host "Creating KVS entry for: $kvskey"
        Write-Host $kvsentry | ConvertFrom-Json -Depth 10
    } else {
        $response = Update-KVSEntry -KvsARN $KvsARN -Key $kvskey -Value $kvsentry
    }
}

Write-Verbose "Finished deploying tenant assets for tenant $TenantKey"

exit 0

