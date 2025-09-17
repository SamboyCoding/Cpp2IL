param (
    [switch]$help,
    [string]$version
)

$ErrorActionPreference = "Stop"

$NugetPackages = @(
    "Cpp2IL.Core"
    "LibCpp2IL"
    "StableNameDotNet"
    "WasmDisassembler"
)

$Plugins = @(
    "Cpp2IL.Plugin.BuildReport"
    "Cpp2IL.Plugin.OrbisPkg"
    "Cpp2IL.Plugin.Pdb"
    "Cpp2IL.Plugin.StrippedCodeRegSupport"
)

$ZippedPlugins = @(
    "Cpp2IL.Plugin.ControlFlowGraph"
)

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

Write-Host "Cleaning up old bin and artifacts directories"

foreach($proj in $NugetPackages) 
{
    $projDir = Join-Path $ProjectRoot $proj
    $projBuildDir = Join-Path $projDir "bin"
    
    if(Test-Path $projBuildDir)
    {
        Remove-Item -Recurse -Force $projBuildDir
    }
}

if(Test-Path $ArtifactsDir)
{
    Remove-Item -Recurse -Force $ArtifactsDir
}

$null = New-Item -ItemType Directory -Force -Path $ArtifactsDir

Write-Host "Building all Nuget packages..."

foreach($proj in $NugetPackages) 
{
    Write-Host "    Building $proj..."
    $projDir = Join-Path $ProjectRoot $proj
    $projBuildDir = Join-Path $projDir "bin\Release"
    
    $null = dotnet build -c Release /p:VersionSuffix=$version $projDir
    
    # Should only be one nupkg file in the bin directory 
    $files = Get-ChildItem $projBuildDir -Filter "*.nupkg"
   
    if($files.Count -ne 1)
    {
        Write-Host "Error: Expected 1 nupkg file in $projBuildDir, found $($files.Count)"
        exit 1
    }
    
    $nupkgFileName = $files[0].Name
    $nupkgFile = $files[0].FullName
    $nupkgDestFile = Join-Path $ArtifactsDir $nupkgFileName
   
    Write-Host "    Copying $nupkgFileName to artifacts directory..."
    
    Copy-Item $nupkgFile $nupkgDestFile
}

cd $MainCommandLineAppDir

$baseVersion = (Select-Xml -XPath "//Project/PropertyGroup/VersionPrefix" -Path ".\Cpp2IL.csproj").Node.InnerText
$fullVersionString = "$baseVersion-$version"
Write-Host "Building Cpp2IL command line executable, release version $fullVersionString"

Write-Host "    Building Cpp2IL - Windows x64, Standalone .NET"

$null = dotnet publish -c Release -f "net9.0" -r "win-x64" /p:VersionSuffix=$version /p:PublishSingleFile=true --self-contained

Write-Host "    Building Cpp2IL - Windows ARM64, Standalone .NET"

$null = dotnet publish -c Release -f "net9.0" -r "win-arm64" /p:VersionSuffix=$version /p:PublishSingleFile=true --self-contained

Write-Host "    Building Cpp2IL - Linux x64, Standalone .NET"

$null = dotnet publish -c Release -f "net9.0" -r "linux-x64" /p:VersionSuffix=$version /p:PublishSingleFile=true --self-contained

Write-Host "    Building Cpp2IL - Linux ARM64, Standalone .NET"

$null = dotnet publish -c Release -f "net9.0" -r "linux-arm64" /p:VersionSuffix=$version /p:PublishSingleFile=true --self-contained

Write-Host "    Building Cpp2IL - MacOS x64, Standalone .NET"

$null = dotnet publish -c Release -f "net9.0" -r "osx-x64" /p:VersionSuffix=$version /p:PublishSingleFile=true --self-contained

Write-Host "    Building Cpp2IL - MacOS ARM64, Standalone .NET"

$null = dotnet publish -c Release -f "net9.0" -r "osx-arm64" /p:VersionSuffix=$version /p:PublishSingleFile=true --self-contained

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

CopyAndRename "net9.0" "win-x64" "Windows" ".exe"
CopyAndRename "net9.0" "win-arm64" "Windows-ARM64" ".exe"
CopyAndRename "net9.0" "linux-x64" "Linux" ""
CopyAndRename "net9.0" "linux-arm64" "Linux-ARM64" ""
CopyAndRename "net9.0" "osx-x64" "OSX" ""
CopyAndRename "net9.0" "osx-arm64" "OSX-ARM64" ""
ZipAndRename "net472" "win-x64" "Windows-Netframework472" ".exe"

Set-Location $ProjectRoot

Write-Host "Building plugins..."

foreach($plugin in $Plugins) 
{
    Write-Host "    Building $plugin..."
    $pluginDir = Join-Path $ProjectRoot $plugin
    $pluginBuildDir = Join-Path $pluginDir "bin\Release"
    
    if(Test-Path $pluginBuildDir)
    {
        Remove-Item -Recurse -Force $pluginBuildDir
    }
    
    $null = dotnet build -c Release $pluginDir
    
    $directories = Get-ChildItem $pluginBuildDir -Directory
    $pluginBuildDir = $directories[0].FullName
    
    $pluginFileName = "$plugin.dll"
    $pluginFile = Join-Path $pluginBuildDir $pluginFileName
    $pluginDestFile = Join-Path $ArtifactsDir $pluginFileName
    
    Write-Host "    Copying $pluginFileName to artifacts directory..."
    Copy-Item $pluginFile $pluginDestFile
}

foreach($plugin in $ZippedPlugins) 
{
    Write-Host "    Building $plugin..."
    $pluginDir = Join-Path $ProjectRoot $plugin
    $pluginBuildDir = Join-Path $pluginDir "bin\Release"
    
    if(Test-Path $pluginBuildDir)
    {
        Remove-Item -Recurse -Force $pluginBuildDir
    }
    
    $null = dotnet build -c Release $pluginDir
    
    $directories = Get-ChildItem $pluginBuildDir -Directory
    $pluginBuildDir = $directories[0].FullName
   
    Write-Host "    Zipping $pluginFileName to artifacts directory..."
    $zipFileName = "$plugin.zip"
    $zipFile = Join-Path $ArtifactsDir $zipFileName
    $null = Compress-Archive -Path $pluginBuildDir\* -DestinationPath $zipFile
}

Write-Host "Done!"
