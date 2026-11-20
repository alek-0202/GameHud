[CmdletBinding()]
param(
    [string]$SolutionPath = "backend/GamesHud.sln"
)

$scanLines = & dotnet list $SolutionPath package --vulnerable --include-transitive --format json 2>&1
$scanExitCode = $LASTEXITCODE

if ($scanExitCode -ne 0) {
    Write-Error "NuGet vulnerability scanning is unavailable. Check package restore, network access, and advisory source availability."
    exit $scanExitCode
}

try {
    $report = $scanLines | Out-String | ConvertFrom-Json
}
catch {
    Write-Error "NuGet vulnerability scanning returned an unreadable report."
    exit 2
}

$severityRanks = @{
    Low = 1
    Moderate = 2
    High = 3
    Critical = 4
}
$findings = @()

foreach ($project in $report.projects) {
    foreach ($framework in $project.frameworks) {
        $packages = @($framework.topLevelPackages) + @($framework.transitivePackages)

        foreach ($package in $packages | Where-Object { $null -ne $_ }) {
            foreach ($vulnerability in $package.vulnerabilities) {
                if (-not $severityRanks.ContainsKey($vulnerability.severity)) {
                    Write-Error "NuGet vulnerability scanning returned an unknown severity."
                    exit 2
                }

                $findings += [pscustomobject]@{
                    Project = Split-Path -Leaf $project.path
                    Framework = $framework.framework
                    Package = $package.id
                    Version = $package.resolvedVersion
                    Severity = $vulnerability.severity
                    SeverityRank = $severityRanks[$vulnerability.severity]
                    AdvisoryUrl = $vulnerability.advisoryurl
                }
            }
        }
    }
}

foreach ($finding in $findings) {
    Write-Host ("{0} [{1}]: {2} {3} - {4} - {5}" -f `
        $finding.Project,
        $finding.Framework,
        $finding.Package,
        $finding.Version,
        $finding.Severity,
        $finding.AdvisoryUrl)
}

$blockingFindings = @($findings | Where-Object { $_.SeverityRank -ge $severityRanks.High })
Write-Host ("NuGet vulnerability scan: {0} finding(s), {1} High/Critical blocking finding(s)." -f `
    $findings.Count,
    $blockingFindings.Count)

if ($blockingFindings.Count -gt 0) {
    exit 1
}
