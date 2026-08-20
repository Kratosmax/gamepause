param(
    [string]$Version = '1.1.3',
    [string]$Runtime = 'win-x64',
    [string]$RuntimePackageSource = 'https://api.nuget.org/v3/index.json',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseDirectory = Join-Path $projectRoot 'temp\release'
$fullOutputDirectory = Join-Path $releaseDirectory "GamePause-$Version-Full"
$liteOutputDirectory = Join-Path $releaseDirectory "GamePause-$Version-Lite"
$fullArchivePath = Join-Path $releaseDirectory "GamePause-$Version-Full.zip"
$liteArchivePath = Join-Path $releaseDirectory "GamePause-$Version-Lite.zip"
$fullInstallerPath = Join-Path $releaseDirectory "GamePause-$Version-Full-Setup.exe"
$liteInstallerPath = Join-Path $releaseDirectory "GamePause-$Version-Lite-Setup.exe"
$solution = Join-Path $projectRoot 'GamePause.sln'
$nugetConfig = Join-Path $projectRoot 'NuGet.Config'
$channelFileName = 'distribution-channel.txt'
$projects = @(
    (Join-Path $projectRoot 'src\GamePause.App\GamePause.App.csproj'),
    (Join-Path $projectRoot 'src\GamePause.Watchdog\GamePause.Watchdog.csproj'),
    (Join-Path $projectRoot 'src\GamePause.Updater\GamePause.Updater.csproj')
)

$projectVersion = ([xml](Get-Content -Raw $projects[0])).Project.PropertyGroup.Version
if ($projectVersion -ne $Version) {
    throw "Requested version $Version does not match GamePause.App version $projectVersion."
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
foreach ($path in @(
    $fullOutputDirectory,
    $liteOutputDirectory,
    $fullArchivePath,
    $liteArchivePath,
    $fullInstallerPath,
    $liteInstallerPath
)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

dotnet restore $solution --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
dotnet build $solution --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
dotnet run --project (Join-Path $projectRoot 'tests\GamePause.CoreTests\GamePause.CoreTests.csproj') `
    --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
# Lite is the default local validation package. It is framework-dependent, so it
# carries no desktop-runtime satellite language directories of its own.
foreach ($project in $projects) {
    dotnet publish $project --configuration Release --no-restore --output $liteOutputDirectory `
        --self-contained false '-p:SatelliteResourceLanguages=zh-Hans%3Ben'
    if ($LASTEXITCODE -ne 0) { throw "Lite publish failed for $project." }
}
$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $liteOutputDirectory $channelFileName), "lite`n", $utf8NoBom)

# Full remains self-contained and keeps every satellite resource supplied by the
# Microsoft desktop runtime so it can run without a separately installed runtime.
foreach ($project in $projects) {
    dotnet restore $project --runtime $Runtime --configfile $nugetConfig --source $RuntimePackageSource
    if ($LASTEXITCODE -ne 0) { throw "Runtime restore failed for $project." }
    dotnet publish $project --configuration Release --runtime $Runtime --self-contained true `
        --no-restore --output $fullOutputDirectory -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw "Full publish failed for $project." }
}
[IO.File]::WriteAllText((Join-Path $fullOutputDirectory $channelFileName), "full`n", $utf8NoBom)

if (Test-Path -LiteralPath (Join-Path $liteOutputDirectory 'coreclr.dll')) {
    throw 'Lite package unexpectedly contains the .NET runtime.'
}
if (-not (Test-Path -LiteralPath (Join-Path $fullOutputDirectory 'coreclr.dll'))) {
    throw 'Full package does not contain the .NET runtime.'
}
$unexpectedLiteLanguages = Get-ChildItem -LiteralPath $liteOutputDirectory -Directory |
    Where-Object { $_.Name -ne 'zh-Hans' }
if ($unexpectedLiteLanguages) {
    throw "Lite package contains unexpected language directories: $($unexpectedLiteLanguages.Name -join ', ')"
}

dotnet (Join-Path $liteOutputDirectory 'GamePause.Updater.dll') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Packaged Lite updater self-test failed.' }

Compress-Archive -Path (Join-Path $fullOutputDirectory '*') -DestinationPath $fullArchivePath
Compress-Archive -Path (Join-Path $liteOutputDirectory '*') -DestinationPath $liteArchivePath

if (-not $SkipInstaller) {
    $isccCandidates = @(
        $env:ISCC_EXE,
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $iscc = $isccCandidates | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 compiler was not found. Set ISCC_EXE or use -SkipInstaller.' }
    & $iscc "/DAppVersion=$Version" "/DSourceDir=$fullOutputDirectory" "/DOutputDir=$releaseDirectory" `
        '/DDistribution=Full' '/DIsLite=0' `
        (Join-Path $projectRoot 'installer\GamePause.iss')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $fullInstallerPath)) {
        throw 'Full installer build failed.'
    }
    & $iscc "/DAppVersion=$Version" "/DSourceDir=$liteOutputDirectory" "/DOutputDir=$releaseDirectory" `
        '/DDistribution=Lite' '/DIsLite=1' `
        (Join-Path $projectRoot 'installer\GamePause.iss')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $liteInstallerPath)) {
        throw 'Lite installer build failed.'
    }
}

Write-Host "Full package published to $fullOutputDirectory"
Write-Host "Lite package published to $liteOutputDirectory (default for local validation)"
Write-Host "Archives created at $fullArchivePath and $liteArchivePath"
if (-not $SkipInstaller) {
    Write-Host "Installers created at $fullInstallerPath and $liteInstallerPath"
}
