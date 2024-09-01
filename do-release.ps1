param (
    [switch]$help,
    [string]$version
)

$ErrorActionPreference = "Stop"

if ($help) {
    Write-Host "Usage: do-release.ps1 [-help] [-version <version>]"
    Write-Host "  -help: Display this help message"
    Write-Host "  -version: The version to build"
    exit
}

if (-not $version) {
    Write-Host "You must specify a version to build"
    exit
}

Write-Host "===CPP2IL RELEASE SCRIPT==="

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$MainCommandLineAppDir = Join-Path $ProjectRoot "Cpp2IL"
$ArtifactsDir = Join-Path $ProjectRoot "artifacts"
$BuildDir = Join-Path $MainCommandLineAppDir "bin"
$ReleaseBuildDir = Join-Path $BuildDir "release"

Write-Host "Cleaning up old build and artifacts directories"
if(Test-Path $ReleaseBuildDir)
{
    Remove-Item -Recurse -Force $ReleaseBuildDir
}

if(Test-Path $ArtifactsDir)
{
    Remove-Item -Recurse -Force $ArtifactsDir
}

cd $MainCommandLineAppDir

$baseVersion = (Select-Xml -XPath "//Project/PropertyGroup/VersionPrefix" -Path ".\Cpp2IL.csproj").Node.InnerText
$fullVersionString = "$baseVersion-$version"
Write-Host "Building Cpp2IL release version $fullVersionString"

Write-Host "    Building Cpp2IL - Windows, Standalone .NET"

$null = dotnet publish -c Release -f "net7.0" -r "win-x64" /p:VersionSuffix=$version /p:PublishSingleFile=true --self-contained

Write-Host "    Building Cpp2IL - Linux, Standalone .NET"

$null = dotnet publish -c Release -f "net7.0" -r "linux-x64" /p:VersionSuffix=$version /p:PublishSingleFile=true --self-contained

Write-Host "    Building Cpp2IL - MacOS, Standalone .NET"

$null = dotnet publish -c Release -f "net7.0" -r "osx-x64" /p:VersionSuffix=$version /p:PublishSingleFile=true --self-contained

Write-Host "    Building Cpp2IL - Windows, .NET Framework"

$null = dotnet publish -c Release -f "net472" -r "win-x64" /p:VersionSuffix=$version

function CopyAndRename($rid, $platform, $releasePlatformString, $extension)
{
    $ridDir = Join-Path $ReleaseBuildDir $rid
    $platformDir = Join-Path $ridDir $platform
    $publishDir = Join-Path $platformDir "publish"
    $file = Join-Path $publishDir "Cpp2IL$extension"
    
    if(Test-Path $file)
    {
        # Cpp2IL-2022.1.0.pre-release-17-Windows.exe
        $destFileName = "Cpp2IL-$fullVersionString-$releasePlatformString$extension"
        Write-Host "    Copying $destFileName..."
        $newFile = Join-Path $ArtifactsDir $destFileName 
        Copy-Item $file $newFile
    }
}

function ZipAndRename($rid, $platform, $releasePlatformString, $extension)
{
    $ridDir = Join-Path $ReleaseBuildDir $rid
    $platformDir = Join-Path $ridDir $platform
    $publishDir = Join-Path $platformDir "publish"
    
    # Zip all files in the publish directory
    $zipFileName = "Cpp2IL-$fullVersionString-$releasePlatformString.zip"
    Write-Host "    Zipping $zipFileName..."
    $zipFile = Join-Path $ArtifactsDir $zipFileName
    $null = Compress-Archive -Path $publishDir\* -DestinationPath $zipFile
}

Write-Host "Moving files to artifacts directory"

$null = New-Item -ItemType Directory -Force -Path $ArtifactsDir

CopyAndRename "net7.0" "win-x64" "Windows" ".exe"
CopyAndRename "net7.0" "linux-x64" "Linux" ""
CopyAndRename "net7.0" "osx-x64" "OSX" ""
ZipAndRename "net472" "win-x64" "Windows-Netframework472" ".exe"

Write-Host "Done!"
Set-Location $ProjectRoot
