<#
.SYNOPSIS
    Prepares a local package cache for Docker builds by copying from NuGet cache
.DESCRIPTION
    This script runs dotnet restore, identifies all required packages, and copies them
    from the NuGet global cache to a local Packages folder for Docker to use.
    This eliminates the need for external package sources during Docker build.
.PARAMETER ProjectPath
    Path to the .csproj file to analyze (relative to Service directory)
.PARAMETER Clean
    If specified, removes the DockerPackages folder before starting
.EXAMPLE
    .\Prepare-DockerPackages.ps1 -ProjectPath "Containers/ChatAppRunner/ChatAppRunner.csproj"
.NOTES
    This script must be run from the Service directory.
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$ProjectPath,

    [Parameter(Mandatory=$false)]
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

Write-Host "======================================"
Write-Host "Docker Package Preparation"
Write-Host "======================================"
Write-Host "Project: $ProjectPath"
Write-Host ""

# Clean existing DockerPackages folder if requested
$dockerPackagesPath = "DockerPackages"
if ($Clean -and (Test-Path $dockerPackagesPath)) {
    Write-Host "Cleaning existing DockerPackages folder..."
    Remove-Item $dockerPackagesPath -Recurse -Force
}

# Create DockerPackages folder
if (-not (Test-Path $dockerPackagesPath)) {
    Write-Host "Creating DockerPackages folder..."
    New-Item -ItemType Directory -Path $dockerPackagesPath -Force | Out-Null
}

# Step 1: Run dotnet restore to ensure all packages are cached
Write-Host "Running dotnet restore to populate NuGet cache..."
dotnet restore $ProjectPath --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet restore failed"
    exit 1
}

# Step 2: Parse project.assets.json to get ALL package dependencies (including transitive)
Write-Host "Analyzing package dependencies from project.assets.json..."

$projectDir = Split-Path $ProjectPath -Parent
$assetsJsonPath = Join-Path $projectDir "obj/project.assets.json"

if (-not (Test-Path $assetsJsonPath)) {
    Write-Error "project.assets.json not found at $assetsJsonPath. Did dotnet restore succeed?"
    exit 1
}

$assetsJson = Get-Content $assetsJsonPath -Raw | ConvertFrom-Json

# Step 3: Get NuGet global packages location
$nugetGlobalPackages = dotnet nuget locals global-packages --list | Select-String "global-packages:" | ForEach-Object { $_.ToString().Replace("global-packages:", "").Trim() }
Write-Host "NuGet global cache: $nugetGlobalPackages"

# Step 3.5: Get local package sources from _Dev/Nuget.Config
$localPackageSources = @()
$devNugetConfig = "../../Nuget.Config"
if (Test-Path $devNugetConfig) {
    Write-Host "Found _Dev/Nuget.Config, checking for local package sources..."
    [xml]$nugetConfig = Get-Content $devNugetConfig
    foreach ($source in $nugetConfig.configuration.packageSources.add) {
        if ($source.value -like "./*" -or $source.value -like "../*") {
            # Resolve relative path from _Dev directory
            $resolvedPath = Resolve-Path (Join-Path "../../" $source.value) -ErrorAction SilentlyContinue
            if ($resolvedPath -and (Test-Path $resolvedPath)) {
                $localPackageSources += $resolvedPath.Path
                Write-Host "  Found local source: $($source.key) -> $($resolvedPath.Path)"
            }
        }
    }
}
Write-Host ""

# Step 4: Collect all unique packages from libraries section
# project.assets.json has a "libraries" section with all packages (NuGet and project references)
$packageDict = @{}

foreach ($library in $assetsJson.libraries.PSObject.Properties) {
    $libName = $library.Name
    $libValue = $library.Value

    # Library names are in format "PackageName/Version" or "ProjectName/Version"
    # Only process NuGet packages (not project references)
    if ($libValue.type -eq "package") {
        if ($libName -match "^(.+)/(.+)$") {
            $pkgName = $matches[1]
            $pkgVersion = $matches[2]

            $key = "$pkgName|$pkgVersion"
            if (-not $packageDict.ContainsKey($key)) {
                $packageDict[$key] = @{
                    Name = $pkgName
                    Version = $pkgVersion
                }
            }
        }
    }
}

Write-Host "Found $($packageDict.Count) unique packages"
Write-Host ""

# Step 5: Copy packages from cache to DockerPackages
Write-Host "Copying packages from NuGet cache and local sources..."
$copiedCount = 0
$skippedCount = 0
$notFoundCount = 0

foreach ($pkg in $packageDict.Values) {
    $pkgName = $pkg.Name.ToLower()
    $pkgVersion = $pkg.Version.ToLower()
    $found = $false

    # Try NuGet global cache first
    # NuGet cache structure: {cache}/{packageName}/{version}/{packageName}.{version}.nupkg
    $cachePath = Join-Path $nugetGlobalPackages "$pkgName/$pkgVersion"
    $nupkgFile = Join-Path $cachePath "$pkgName.$pkgVersion.nupkg"

    if (Test-Path $nupkgFile) {
        $destFile = Join-Path $dockerPackagesPath "$pkgName.$pkgVersion.nupkg"

        if (-not (Test-Path $destFile)) {
            Copy-Item $nupkgFile $destFile -Force
            $copiedCount++
            Write-Host "  [+] $pkgName $pkgVersion (from cache)"
            $found = $true
        } else {
            $skippedCount++
            $found = $true
        }
    }

    # If not found in cache, try local package sources
    if (-not $found) {
        foreach ($localSource in $localPackageSources) {
            # Local sources have flat structure: {source}/{packageName}.{version}.nupkg
            $localNupkg = Join-Path $localSource "$pkgName.$pkgVersion.nupkg"
            if (Test-Path $localNupkg) {
                $destFile = Join-Path $dockerPackagesPath "$pkgName.$pkgVersion.nupkg"

                if (-not (Test-Path $destFile)) {
                    Copy-Item $localNupkg $destFile -Force
                    $copiedCount++
                    Write-Host "  [+] $pkgName $pkgVersion (from local)"
                    $found = $true
                } else {
                    $skippedCount++
                    $found = $true
                }
                break
            }
        }
    }

    if (-not $found) {
        Write-Host "  [!] Not found: $pkgName $pkgVersion" -ForegroundColor Yellow
        $notFoundCount++
    }
}

Write-Host ""
Write-Host "======================================"
Write-Host "Package preparation complete!"
Write-Host "Copied: $copiedCount packages"
Write-Host "Skipped: $skippedCount packages (already present)"
if ($notFoundCount -gt 0) {
    Write-Host "Not Found: $notFoundCount packages" -ForegroundColor Yellow
    Write-Host "  (These may be available from nuget.org during Docker build)"
}
Write-Host "Location: $dockerPackagesPath"
Write-Host "======================================"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Update nuget.config to point to DockerPackages"
Write-Host "  2. Run docker build"
Write-Host ""
