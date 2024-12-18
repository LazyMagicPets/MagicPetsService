# This script deploys a stack defining a tenancy
# Note: This script is generated. See the template 
# AWSTemplates/Snippets/DeployTenantsStack.ps1
# https://docs.aws.amazon.com/powershell/latest/reference/
# Generated by __ResourceGenerator__ from __TemplateSource__
param( 
    [Parameter(Mandatory=$true)]
    [string]$TenantKey, 
    [Parameter(Mandatory=$true)]
    [string]$SubDomain,
    [string]$DefaultPageUrl,
    [string]$Guid,
    [string]$RootDomain
)

if (-not $PSBoundParameters.ContainsKey('DefaultPageUrl')) {
    $DefaultPageUrl = Read-Host -Prompt "DefaultPageUrl [none]"
    if ([string]::IsNullOrEmpty($DefaultPageUrl)) {
        $DefaultPageUrl = "none"
    }
}

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
$OriginAccessControlId = $config.OriginAccessControlId


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
$targetStack = $config.SystemName + "-cfpolicies"
$CFPolicyStackOutputDict = Get-StackOutputs $targetStack
#Display-OutputDictionary -Dictionary $CFPolicyStackOutputDict -Title "CFPolicy Stack Outputs"

# Get sytem assets stack outputs 
$targetStack = $config.SystemName + "-assets-system"
$SystemAssetsStackOutputDict = Get-StackOutputs $targetStack
#Display-OutputDictionary -Dictionary $SystemAssetsStackOutputDict -Title "Assets-system Stack Outputs"

# Get tenant assets stack outputs 
$targetStack = $config.SystemName + "-assets-" + $TenantKey
$AssetsStackOutputDict = Get-StackOutputs $targetStack
#Display-OutputDictionary -Dictionary $AssetsStackOutputDict -Title "Assets-$TenantKey Stack Outputs"


# Get webapp stack(s) outputs 
# WebAppStackOutputs __webappstackoutputs__

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
    "WebSocketApiParameter" = $ServiceStackOutputDict["WebSocketApi"]
    "ConfigFunctionArnParameter" = $ServiceStackOutputDict["ConfigFunctionArn"]

    "OriginRequestPolicyIdParameter" = $CFPolicyStackOutputDict["OriginRequestPolicyId"]
    "CachePolicyIdParameter" = $CFPolicyStackOutputDict["CachePolicyId"]
    "CacheByHeaderPolicyIdParameter" = $CFPolicyStackOutputDict["CacheByHeaderPolicyId"]
    "ApiCachePolicyIdParameter" = $CFPolicyStackOutputDict["ApiCachePolicyId"]
    "RequestFunctionArnParameter" = $CFPolicyStackOutputDict["RequestFunctionArn"]
    "RequestPrefixFunctionArnParameter" = $CFPolicyStackOutputDict["RequestPrefixFunctionArn"]
    "ResponseFunctionArnParameter" = $CFPolicyStackOutputDict["ResponseFunctionArn"]

    "SystemAssetsBucketNameParameter" = $SystemAssetsStackOutputDict["AssetsBucketName"]
    "AssetsBucketNameParameter" = $AssetsStackOutputDict["AssetsBucketName"]
    "CDNLogBucketNameParameter" = $AssetsStackOutputDict["CDNLogBucketName"]
    "DefaultPageUrlParameter" = $DefaultPageUrl

    # WebApps __webapps__

    # Apis __apis__

    # Authentications __auths__

}
#Display-OutputDictionary -Dictionary $ParametersDict -Title "Parameters Dictionary"

$parameters = ConvertTo-ParameterOverrides -parametersDict $ParametersDict
Write-Host "Deploying the stack $StackName" 
sam deploy `
--template-file __templatename__ `
--s3-bucket $ArtifactsBucket `
--stack-name $StackName `
--parameter-overrides $parameters `
--capabilities CAPABILITY_IAM CAPABILITY_AUTO_EXPAND `
--profile $Profile 
