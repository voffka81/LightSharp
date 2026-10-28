[CmdletBinding()]
param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectFile = Join-Path $repoRoot "src\LightSharp\LightSharp.csproj"
$releaseDirectory = Join-Path $repoRoot "artifacts\release"
$stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "LightSharp-publish-$([guid]::NewGuid().ToString('N'))"

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "Project file was not found: $projectFile"
}

if ($RuntimeIdentifier -notmatch '^[A-Za-z0-9.-]+$') {
    throw "RuntimeIdentifier may contain only letters, numbers, dots, and hyphens."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $commit = & git -C $repoRoot rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) { $commit = "local" }
    $Version = "test-$($commit.Trim())-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

if ($Version -notmatch '^[A-Za-z0-9._-]+$') {
    throw "Version may contain only letters, numbers, dots, underscores, and hyphens."
}

$null = New-Item -ItemType Directory -Path $releaseDirectory -Force
$artifactDirectory = Join-Path $releaseDirectory "LightSharp-$RuntimeIdentifier-$Version"
$null = New-Item -ItemType Directory -Path $stagingDirectory

try {
    Write-Host "Publishing LightSharp ($RuntimeIdentifier, framework-dependent)..."
    & dotnet publish $projectFile `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained false `
        --output $stagingDirectory `
        --nologo `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $releaseRoot = [System.IO.Path]::GetFullPath($releaseDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $artifactPath = [System.IO.Path]::GetFullPath($artifactDirectory)
    if (-not $artifactPath.StartsWith($releaseRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifact path must stay inside $releaseDirectory."
    }

    if (Test-Path -LiteralPath $artifactPath) {
        Remove-Item -LiteralPath $artifactPath -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $artifactPath

    # Keep runtime files, but omit debug symbols and documentation to reduce the test artifact.
    $stagingRoot = [System.IO.Path]::GetFullPath($stagingDirectory).TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
    Get-ChildItem -LiteralPath $stagingDirectory -File -Recurse | ForEach-Object {
        if ($_.Extension -notin @(".pdb", ".xml")) {
            $relativePath = $_.FullName.Substring($stagingRoot.Length)
            $destinationPath = Join-Path $artifactPath $relativePath
            $destinationParent = Split-Path -Parent $destinationPath
            $null = New-Item -ItemType Directory -Path $destinationParent -Force
            Copy-Item -LiteralPath $_.FullName -Destination $destinationPath
        }
    }

    $artifactBytes = (Get-ChildItem -LiteralPath $artifactPath -File -Recurse |
        Measure-Object -Property Length -Sum).Sum
    Write-Host "Created: $artifactPath"
    Write-Host ("Artifact folder size: {0:N1} MB" -f ($artifactBytes / 1MB))
    Write-Host "Run LightSharp.exe from this folder. The target machine needs .NET 10 installed."
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
