# This script deploys a stack defining a tenancy
# Note: This script is generated. See the template 
# AWSTemplates/Snippets/DeployTenantsStack.ps1
# https://docs.aws.amazon.com/powershell/latest/reference/
param( 
    [Parameter(Mandatory=$true)]
    [string]$TenantKey, 
    [Parameter(Mandatory=$true)]
    [string]$SubDomain,
    [string]$Guid,
    [string]$RootDomain
)

Import-Module powershell-yaml
Import-Module AWSPowerShell.NetCore

function Display-OutputDictionary {
    param (
        [Parameter(Mandatory=$true)]
        [hashtable]$Dictionary,
        
        [Parameter(Mandatory=$false)]
        [string]$Title = "Stack Outputs"
    )
    
    Write-Host $Title -ForegroundColor Cyan
    Write-Host "------------------------" -ForegroundColor Cyan
    
    $Dictionary.GetEnumerator() | Sort-Object Key | ForEach-Object {
        Write-Host "$($_.Key):" -ForegroundColor Green -NoNewline
        Write-Host " $($_.Value)"
    }
    
    Write-Host "------------------------`n" -ForegroundColor Cyan
}
function Get-StackOutputs {
	param (
	    [string]$SourceStackName
        )

    $stack = Get-CFNStack -StackName $SourceStackName -ProfileName $Profile
    $outputDictionary = @{}
    foreach($output in $stack.Outputs) {
        $outputDictionary[$output.OutputKey] = $output.OutputValue
    }
    return $outputDictionary
}
function ConvertTo-ParameterOverrides {
    param ([hashtable]$parametersDict)
    $overrides = @()
    foreach($key in $parametersDict.Keys) {
        $value = $parametersDict[$key]
		$overrides += "$key='$value'"
	}
    return $overrides -join " "
}

# Load configuration from YAML file
$filePath = "..\..\..\serviceconfig.yaml"
if(-not (Test-Path $filePath))
{
	Write-Host "Please create a serviceconfig.yaml file above the solution folder."
	Write-Host "Copy the serviceconfig.yaml.template file and update the values in the new file."
	exit
}

$config = Get-Content -Path $filePath | ConvertFrom-Yaml
$SystemGuid = $config.SystemGuid
if(-not $Guid.HasValue) {
	$Guid = $SystemGuid
}
$SystemName = $config.SystemName
$StackName = $config.SystemName + "-tenant-" + $TenantKey
$ArtifactsBucket = $config.SystemName + "-artifacts-" + $config.SystemGuid
$Profile = $config.Profile
$Environment = $config.Environment
$ConfigRootDomain = $config.RootDomain
if(-not $RootDomain.HasValue) {
	$RootDomain = $ConfigRootDomain
}
$HostedZoneId = $config.HostedZoneId
$AcmCertificateArn = $config.AcmCertificateArn

if($SystemGuid -like "yourguid")
{
	Write-Host "Please update the serviceconfig.yaml file with your system guid"
	exit
}

# Get service stack outputs
$targetStack = $config.SystemName + "-service"
$ServiceStackOutputDict = Get-StackOutputs $targetStack
#Display-OutputDictionary -Dictionary $ServiceStackOutputDict -Title "Service Stack Outputs"

# Get policies stack outputs 
$targetStack = $config.SystemName + "-policies"
$PolicyStackOutputDict = Get-StackOutputs $targetStack
#Display-OutputDictionary -Dictionary $PolicyStackOutputDict -Title "Policy Stack Outputs"

# Get sytem assets stack outputs 
$targetStack = $config.SystemName + "-assets-system"
$SystemAssetsStackOutputDict = Get-StackOutputs $targetStack
#Display-OutputDictionary -Dictionary $SystemAssetsStackOutputDict -Title "Assets-system Stack Outputs"

# Get tenant assets stack outputs 
$targetStack = $config.SystemName + "-assets-" + $TenantKey
$AssetsStackOutputDict = Get-StackOutputs $targetStack
#Display-OutputDictionary -Dictionary $AssetsStackOutputDict -Title "Assets-$TenantKey Stack Outputs"


# Get webapp stack(s) outputs 
# WebAppStackOutputs 
$targetStack = $config.SystemName + "-webapp-consumerapp" 
$ConsumerAppStackOutputDict = Get-StackOutputs $targetStack
Display-OutputDictionary -Dictionary $ConsumerAppStackOutputDict -Title "storeapp Stack Outputs"
                    

# Create the parameters dictionary
$ParametersDict = @{
    "SystemNameParameter" = $SystemName
    "TenantKeyParameter" = $TenantKey
    "SubDomainParameter" = $SubDomain
    "GuidParameter" = $Guid
    "RootDomainParameter" = $RootDomain
    "EnvironmentParameter" = $Environment
    "HostedZoneIdParameter" = $HostedZoneId
    "AcmCertificateArnParameter" = $AcmCertificateArn

    "OriginRequestPolicyIdParameter" = $PolicyStackOutputDict["OriginRequestPolicyId"]
    "CachePolicyIdParameter" = $PolicyStackOutputDict["CachePolicyId"]
    "ResponseHeadersPolicyIdParameter" = $PolicyStackOutputDict["ResponseHeadersPolicyId"]
    "RequestFunctionArnParameter" = $PolicyStackOutputDict["RequestFunctionArn"]
    "RequestPrefixFunctionArnParameter" = $PolicyStackOutputDict["RequestPrefixFunctionArn"]
    "ResponseFunctionArnParameter" = $PolicyStackOutputDict["ResponseFunctionArn"]

    "SystemAssetsBucketNameParameter" = $SystemAssetsStackOutputDict["AssetsBucketName"]
    "AssetsBucketNameParameter" = $AssetsStackOutputDict["AssetsBucketName"]
    "CDNLogBucketNameParameter" = $SystemAssetsStackOutputDict["CDNLogBucketName"]

    # WebApps 
    "ConsumerAppBucketNameParameter" = $ConsumerAppStackOutputDict["AppBucket"]


    # Apis 
    "ConsumerApiIdParameter" = $ServiceStackOutputDict["ConsumerApiId"]

    "PublicApiIdParameter" = $ServiceStackOutputDict["PublicApiId"]

    "WebSocketApiIdParameter" = $ServiceStackOutputDict["WebSocketApiId"]


    # Authentications 
    "ConsumerAuthUserPoolNameParameter" = $ServiceStackOutputDict["ConsumerAuthUserPoolName"]
    "ConsumerAuthUserPoolIdParameter" = $ServiceStackOutputDict["ConsumerAuthUserPoolId"]
    "ConsumerAuthUserPoolClientIdParameter" = $ServiceStackOutputDict["ConsumerAuthUserPoolClientId"]
    "ConsumerAuthIdentityPoolIdParameter" = $ServiceStackOutputDict["ConsumerAuthIdentityPoolId"]
    "ConsumerAuthSecurityLevelParameter" = $ServiceStackOutputDict["ConsumerAuthSecurityLevel"]


}
#Display-OutputDictionary -Dictionary $ParametersDict -Title "Parameters Dictionary"

$parameters = ConvertTo-ParameterOverrides -parametersDict $ParametersDict
Write-Host "Deploying the stack $StackName" 
sam deploy `
--template-file sam.StoreTenancy.g.yaml `
--s3-bucket $ArtifactsBucket `
--stack-name $StackName `
--parameter-overrides $parameters `
--capabilities CAPABILITY_IAM CAPABILITY_AUTO_EXPAND `
--profile $Profile 