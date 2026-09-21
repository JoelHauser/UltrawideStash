<#
.SYNOPSIS
    Build both halves, check them, and optionally install into an SPT folder.

.DESCRIPTION
    The mod is two assemblies that install to two different places:

      SPT_Runtime/user/mods/UltrawideStash/UltrawideStash.Server.dll   the width
      BepInEx/plugins/UltrawideStash.Probe.dll                         the measurement

    Refuses to pack if the csproj Version and the version baked into the source
    disagree, because a zip whose name does not match the DLL it contains is the
    thing that makes a bug report unanswerable.

    Run through PowerShell, not Bash -- the C:\HUH path mangling trap that bites the
    sibling repos applies here too.

.EXAMPLE
    scripts\pack.ps1 -SPTPath C:\HUH
    scripts\pack.ps1 -SPTPath C:\HUH -Install
#>
[CmdletBinding()]
param(
    [string] $SPTPath = 'C:\HUH',
    [switch] $Install,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$serverProj = Join-Path $root 'src\UltrawideStash.Server\UltrawideStash.Server.csproj'
$probeProj = Join-Path $root 'src\UltrawideStash.Probe\UltrawideStash.Probe.csproj'
$testProj = Join-Path $root 'tests\UltrawideStash.Server.Tests\UltrawideStash.Server.Tests.csproj'

function Get-XmlVersion {
    param([string] $Path)

    $xml = [xml](Get-Content $Path -Raw)
    return ($xml.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
}

function Get-SourceVersion {
    param([string] $Path, [string] $Pattern)

    $text = Get-Content $Path -Raw

    if ($text -match $Pattern) { return $matches[1] }

    return $null
}

# ---- version agreement -------------------------------------------------------

$serverCsprojVersion = Get-XmlVersion $serverProj
$serverMetaVersion = Get-SourceVersion (Join-Path $root 'src\UltrawideStash.Server\ModMetadata.cs') 'new\("([0-9.]+)"\)'

$probeCsprojVersion = Get-XmlVersion $probeProj
$probeSourceVersion = Get-SourceVersion (Join-Path $root 'src\UltrawideStash.Probe\ProbePlugin.cs') 'PluginVersion = "([0-9.]+)"'

Write-Host "server: csproj $serverCsprojVersion, ModMetadata $serverMetaVersion" -ForegroundColor Cyan
Write-Host "probe:  csproj $probeCsprojVersion, PluginVersion $probeSourceVersion" -ForegroundColor Cyan

if ($serverCsprojVersion -ne $serverMetaVersion) {
    Write-Host "Server version disagreement: csproj says $serverCsprojVersion, ModMetadata says $serverMetaVersion." -ForegroundColor Red
    exit 2
}

if ($probeCsprojVersion -ne $probeSourceVersion) {
    Write-Host "Probe version disagreement: csproj says $probeCsprojVersion, ProbePlugin says $probeSourceVersion." -ForegroundColor Red
    exit 2
}

if ($serverCsprojVersion -ne $probeCsprojVersion) {
    Write-Host "The two halves are at different versions ($serverCsprojVersion vs $probeCsprojVersion). They ship together." -ForegroundColor Red
    exit 2
}

$version = $serverCsprojVersion

# ---- build -------------------------------------------------------------------

Write-Host ''
Write-Host "Building $version" -ForegroundColor Cyan

dotnet build $serverProj -c Release -v minimal --nologo
if ($LASTEXITCODE -ne 0) { Write-Host 'Server build failed.' -ForegroundColor Red; exit 1 }

dotnet build $probeProj -c Release -v minimal --nologo "-p:SPTPath=$SPTPath"
if ($LASTEXITCODE -ne 0) { Write-Host 'Probe build failed.' -ForegroundColor Red; exit 1 }

# ---- checks ------------------------------------------------------------------

if (-not $SkipTests) {
    Write-Host ''
    Write-Host 'Running the logic suite' -ForegroundColor Cyan

    dotnet test $testProj -v minimal --nologo
    if ($LASTEXITCODE -ne 0) { Write-Host 'Logic tests failed.' -ForegroundColor Red; exit 1 }

    Write-Host ''
    Write-Host 'Checking the stash ids against the database' -ForegroundColor Cyan

    & (Join-Path $PSScriptRoot 'test-database.ps1') -SPTPath $SPTPath
    if ($LASTEXITCODE -ne 0) { Write-Host 'Database check failed.' -ForegroundColor Red; exit 1 }
}

# ---- reference hygiene -------------------------------------------------------
#
# The probe must not carry an Assembly-CSharp reference. If it ever does, it will
# load on this machine and fail on a launched install where the delta has renamed
# the types -- the exact failure the no-reference design exists to prevent.

$probeDll = Join-Path $root 'src\UltrawideStash.Probe\bin\Release\UltrawideStash.Probe.dll'
$cecil = Join-Path $SPTPath 'SPT_Runtime\Mono.Cecil.dll'

if ((Test-Path $cecil) -and (Test-Path $probeDll)) {
    Add-Type -Path $cecil
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($probeDll)
    $refs = $asm.MainModule.AssemblyReferences | ForEach-Object { $_.Name }
    $asm.Dispose()

    $bad = $refs | Where-Object { $_ -match 'Assembly-CSharp|^spt-' }

    if ($bad) {
        Write-Host ''
        Write-Host "Probe references game code it must not: $($bad -join ', ')" -ForegroundColor Red
        exit 1
    }

    Write-Host ''
    Write-Host "Probe references: $($refs -join ', ')" -ForegroundColor DarkGray
    Write-Host 'No Assembly-CSharp, no spt-* reference.' -ForegroundColor Green
}

# ---- stage -------------------------------------------------------------------

$dist = Join-Path $root "dist\$version"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

# SPT 4.x keeps server mods under SPT_Runtime\user\mods, NOT a root-level user\.
# The zip is unpacked over the SPT root, so it has to carry that full path or the
# mod lands somewhere the server never looks and fails silently.
$serverOut = Join-Path $dist 'SPT_Runtime\user\mods\UltrawideStash'
$pluginOut = Join-Path $dist 'BepInEx\plugins'

New-Item -ItemType Directory -Force -Path $serverOut | Out-Null
New-Item -ItemType Directory -Force -Path $pluginOut | Out-Null

Copy-Item (Join-Path $root 'src\UltrawideStash.Server\bin\Release\net10.0\UltrawideStash.Server.dll') $serverOut
Copy-Item $probeDll $pluginOut

# The zip is unpacked over the SPT root, so every staged path must be one the game or
# server actually reads. Getting this wrong fails silently, which is the worst way.
$expected = @(
    'SPT_Runtime\user\mods\UltrawideStash\UltrawideStash.Server.dll',
    'BepInEx\plugins\UltrawideStash.Probe.dll'
)

$staged = Get-ChildItem $dist -Recurse -File | ForEach-Object {
    $_.FullName.Substring($dist.Length + 1)
}

foreach ($want in $expected) {
    if ($staged -notcontains $want) {
        Write-Host ''
        Write-Host "Staging is wrong: expected '$want' and it is not there." -ForegroundColor Red
        Write-Host "Staged: $($staged -join ', ')"
        exit 1
    }
}

Write-Host ''
Write-Host "Staged to dist\$version" -ForegroundColor Green
Get-ChildItem $dist -Recurse -File | ForEach-Object {
    Write-Host "  $($_.FullName.Substring($dist.Length + 1))"
}

# ---- zip ---------------------------------------------------------------------

$releases = Join-Path $root 'releases'
New-Item -ItemType Directory -Force -Path $releases | Out-Null

$zip = Join-Path $releases "UltrawideStash_V$version.zip"

if (Test-Path $zip) { Remove-Item $zip -Force }

Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip

Write-Host ''
Write-Host "Packed $zip" -ForegroundColor Green

# ---- install -----------------------------------------------------------------

if ($Install) {
    $serverDest = Join-Path $SPTPath 'SPT_Runtime\user\mods\UltrawideStash'
    $pluginDest = Join-Path $SPTPath 'BepInEx\plugins'

    if (-not (Test-Path $pluginDest)) {
        Write-Host "No BepInEx\plugins under $SPTPath." -ForegroundColor Red
        exit 2
    }

    New-Item -ItemType Directory -Force -Path $serverDest | Out-Null

    Copy-Item (Join-Path $serverOut 'UltrawideStash.Server.dll') $serverDest -Force
    Copy-Item (Join-Path $pluginOut 'UltrawideStash.Probe.dll') $pluginDest -Force

    # Never clobber a config the player has edited. The server writes a default one
    # on first run if it is absent.
    Write-Host ''
    Write-Host "Installed to $SPTPath" -ForegroundColor Green
    Write-Host "  $serverDest\UltrawideStash.Server.dll"
    Write-Host "  $pluginDest\UltrawideStash.Probe.dll"
    Write-Host ''
    Write-Host 'Back up SPT_Runtime\user\profiles before you play.' -ForegroundColor Yellow
}
