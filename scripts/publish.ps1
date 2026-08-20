param(
    [string]$Version = '1.0.0',
    [string]$Runtime = 'win-x64',
    [string]$RuntimePackageSource = 'https://api.nuget.org/v3/index.json',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseDirectory = Join-Path $projectRoot 'temp\release'
$outputDirectory = Join-Path $releaseDirectory "GamePause-$Version"
$archivePath = Join-Path $releaseDirectory "GamePause-$Version.zip"
$installerPath = Join-Path $releaseDirectory "GamePause-$Version-Setup.exe"
$solution = Join-Path $projectRoot 'GamePause.sln'
$nugetConfig = Join-Path $projectRoot 'NuGet.Config'
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
foreach ($path in @($outputDirectory, $archivePath, $installerPath)) {
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
dotnet (Join-Path $projectRoot 'src\GamePause.Updater\bin\Release\net8.0-windows\GamePause.Updater.dll') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Updater self-test failed.' }

foreach ($project in $projects) {
    dotnet restore $project --runtime $Runtime --configfile $nugetConfig --source $RuntimePackageSource
    if ($LASTEXITCODE -ne 0) { throw "Runtime restore failed for $project." }
    dotnet publish $project --configuration Release --runtime $Runtime --self-contained true `
        --no-restore --output $outputDirectory -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $project." }
}

Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archivePath

if (-not $SkipInstaller) {
    $isccCandidates = @(
        $env:ISCC_EXE,
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $iscc = $isccCandidates | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 compiler was not found. Set ISCC_EXE or use -SkipInstaller.' }
    & $iscc "/DAppVersion=$Version" "/DSourceDir=$outputDirectory" "/DOutputDir=$releaseDirectory" `
        (Join-Path $projectRoot 'installer\GamePause.iss')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) {
        throw 'Installer build failed.'
    }
}

Write-Host "Published to $outputDirectory"
Write-Host "Archive created at $archivePath"
if (-not $SkipInstaller) { Write-Host "Installer created at $installerPath" }
