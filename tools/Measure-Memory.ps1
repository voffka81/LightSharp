param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [int]$DurationSeconds = 60,
    [int]$IntervalSeconds = 2,
    [string]$OutputPath = "memory-samples.csv"
)

$ErrorActionPreference = "Stop"
if ($DurationSeconds -lt 1 -or $IntervalSeconds -lt 1) { throw "Durations must be positive." }
$samples = [System.Collections.Generic.List[object]]::new()
$end = [DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
do {
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { break }
    $samples.Add([pscustomobject]@{
        TimestampUtc = [DateTimeOffset]::UtcNow.ToString("o")
        ProcessId = $ProcessId
        WorkingSetMiB = [math]::Round($process.WorkingSet64 / 1MB, 2)
        PrivateBytesMiB = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
    })
    if ([DateTimeOffset]::UtcNow -ge $end) { break }
    Start-Sleep -Seconds $IntervalSeconds
} while ($true)

if ($samples.Count -eq 0) { throw "No samples: process $ProcessId is not running." }
$samples | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding utf8
$ws = $samples | Measure-Object -Property WorkingSetMiB -Average -Maximum
$private = $samples | Measure-Object -Property PrivateBytesMiB -Average -Maximum
Write-Host "Samples: $($samples.Count); Working set avg/peak: $([math]::Round($ws.Average,2)) / $($ws.Maximum) MiB"
Write-Host "Private bytes avg/peak: $([math]::Round($private.Average,2)) / $($private.Maximum) MiB"
Write-Host "CSV: $OutputPath"
