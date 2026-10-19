param([switch]$Verify)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = Join-Path $repoRoot 'artifacts'
# Fresh output prevents old executables, settings or reports from entering a new archive.
$buildRoot = Join-Path $artifactRoot ('build-' + [guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $buildRoot 'portable'
New-Item -ItemType Directory -Force $publishRoot | Out-Null
dotnet publish (Join-Path $repoRoot 'src/JetPane/JetPane.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --source https://api.nuget.org/v3/index.json -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item (Join-Path $repoRoot 'README.md') (Join-Path $publishRoot 'README.md') -Force
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $publishRoot 'LICENSE') -Force
function Invoke-Verification([string]$Exe, [string]$Argument, [string]$Report) {
    $process = Start-Process -FilePath $Exe -ArgumentList $Argument -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()
        throw "$Argument timed out after 60 seconds. Only the owned test process was stopped."
    }
    if ($process.ExitCode -ne 0) { throw "$Argument failed with exit code $($process.ExitCode). See $Report" }
    if (-not (Test-Path -LiteralPath $Report)) { throw "$Argument did not produce fresh evidence." }
    $lines = @(Get-Content -LiteralPath $Report)
    if (-not ($lines -match '^PASS:') -or ($lines -match '^(FAIL:|System\.)')) { throw "$Argument produced invalid evidence: $Report" }
}
if ($Verify) {
    $exe = Join-Path $publishRoot 'JetPane.exe'
    Invoke-Verification $exe '--state-test' (Join-Path $publishRoot 'state-test.txt')
    Invoke-Verification $exe '--self-test' (Join-Path $publishRoot 'self-test.txt')
    $smokeRoot = Join-Path $buildRoot 'smoke'
    New-Item -ItemType Directory -Force $smokeRoot | Out-Null
    $smokeExe = Join-Path $smokeRoot 'JetPane.exe'
    Copy-Item $exe $smokeExe
    # Exercise real registration with an isolated test binding, without competing for the user's normal hotkeys.
    @{ AutoRestore = $false; Shortcuts = @{ save = 'Ctrl+Alt+Shift+F24'; restore = ''; preview = '' } } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $smokeRoot 'settings.json')
    Invoke-Verification $smokeExe '--smoke-test' (Join-Path $smokeRoot 'smoke-test.txt')
    Copy-Item (Join-Path $smokeRoot 'smoke-test.txt') $publishRoot
}
$files = @('JetPane.exe','Help.html','README.md','LICENSE')
if ($Verify) { $files += @('state-test.txt','self-test.txt','smoke-test.txt') }
$zip = Join-Path $artifactRoot 'JetPane-portable.zip'
Compress-Archive -Path ($files | ForEach-Object { Join-Path $publishRoot $_ }) -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content (Join-Path $artifactRoot 'SHA256SUMS.txt') "$hash  JetPane-portable.zip"
Write-Output "Portable package: $zip"
Write-Output "Fresh verification output: $publishRoot"
