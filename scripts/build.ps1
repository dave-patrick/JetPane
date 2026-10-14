param([switch]$Verify)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = Join-Path $repoRoot 'artifacts'
$publishRoot = Join-Path $artifactRoot 'portable'
New-Item -ItemType Directory -Force $publishRoot | Out-Null
dotnet publish (Join-Path $repoRoot 'src/JetPane/JetPane.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --source https://api.nuget.org/v3/index.json -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item (Join-Path $repoRoot 'README.md') (Join-Path $publishRoot 'README.md') -Force
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $publishRoot 'LICENSE') -Force
if ($Verify) {
    $exe = Join-Path $publishRoot 'JetPane.exe'
    $test = Start-Process $exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw 'Self-test failed. See portable/self-test.txt.' }
    $smokeRoot = Join-Path $artifactRoot 'smoke'
    New-Item -ItemType Directory -Force $smokeRoot | Out-Null
    Copy-Item $exe (Join-Path $smokeRoot 'JetPane.exe') -Force
    $smoke = Start-Process (Join-Path $smokeRoot 'JetPane.exe') -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
    if ($smoke.ExitCode -ne 0) { throw 'Smoke test failed.' }
    Copy-Item (Join-Path $smokeRoot 'smoke-test.txt') $publishRoot -Force
}
$files = @('JetPane.exe','Help.html','README.md','LICENSE')
foreach ($report in @('self-test.txt','smoke-test.txt')) { if (Test-Path (Join-Path $publishRoot $report)) { $files += $report } }
$zip = Join-Path $artifactRoot 'JetPane-portable.zip'
Compress-Archive -Path ($files | ForEach-Object { Join-Path $publishRoot $_ }) -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content (Join-Path $artifactRoot 'SHA256SUMS.txt') "$hash  JetPane-portable.zip"
Write-Output "Portable package: $zip"
