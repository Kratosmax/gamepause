$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$version = '0.9.1'
$releaseDirectory = Join-Path $projectRoot 'temp\release'
$outputDirectory = Join-Path $releaseDirectory "GamePause-$version"
$archivePath = Join-Path $releaseDirectory "GamePause-$version.zip"

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

dotnet restore (Join-Path $projectRoot 'GamePause.sln') --configfile (Join-Path $projectRoot 'NuGet.Config')
dotnet build (Join-Path $projectRoot 'GamePause.sln') --configuration Release --no-restore
dotnet publish (Join-Path $projectRoot 'src\GamePause.App\GamePause.App.csproj') --configuration Release --no-restore --output $outputDirectory
dotnet publish (Join-Path $projectRoot 'src\GamePause.Watchdog\GamePause.Watchdog.csproj') --configuration Release --no-restore --output $outputDirectory
dotnet publish (Join-Path $projectRoot 'src\GamePause.Updater\GamePause.Updater.csproj') --configuration Release --no-restore --output $outputDirectory

Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archivePath

Write-Host "Published to $outputDirectory"
Write-Host "Archive created at $archivePath"
