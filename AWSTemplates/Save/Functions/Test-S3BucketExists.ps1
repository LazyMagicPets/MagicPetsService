function Test-S3BucketExists {
    param (
        [string]$BucketName
    )
    
    try {
        # Try to get just this specific bucket
        $null = Get-S3BucketLocation -BucketName $BucketName -ErrorAction SilentlyContinue
        return $true
    }
    catch {
        if ($_.Exception.Message -like "*The specified bucket does not exist*") {
            return $false
        }
        Write-Verbose "Error checking bucket existence: $_"
        return $false
    }
}