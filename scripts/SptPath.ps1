<#
.SYNOPSIS
    Work out which SPT install to act on, without anybody's machine baked in.

.DESCRIPTION
    Dot-sourced by the build scripts. repair-stash.ps1 carries its own copy on
    purpose: that one ships inside the mod folder and has to keep working when
    every other file in this repo is gone, so it cannot depend on a sibling.

    Resolution order, first hit wins:

      1. -SPTPath, when given
      2. $env:SPT_PATH
      3. the nearest SPT root at or above this script
      4. the nearest SPT root at or above the current directory

    Step 3 is what makes the shipped copy zero-argument: the mod installs to
    <SPT>\SPT_Runtime\user\mods\UltrawideStash, so walking up from the script
    finds the install it was installed into.

    "An SPT root" means a folder holding SPT_Runtime\SPT_Data -- present in every
    install, and not something an unrelated folder has by accident.
#>

function Test-SptRoot {
    param([string] $Path)

    if (-not $Path) { return $false }

    return Test-Path (Join-Path $Path 'SPT_Runtime\SPT_Data')
}

function Find-SptRootUpward {
    param([string] $Start)

    if (-not $Start) { return $null }

    $dir = Resolve-Path -LiteralPath $Start -ErrorAction SilentlyContinue

    if (-not $dir) { return $null }

    $current = Get-Item -LiteralPath $dir

    # Directories only; a file start walks from its folder.
    if (-not $current.PSIsContainer) { $current = $current.Directory }

    while ($current) {
        if (Test-SptRoot $current.FullName) { return $current.FullName }

        $current = $current.Parent
    }

    return $null
}

function Resolve-SptPath {
    param(
        [string] $SPTPath,
        [switch] $Quiet
    )

    if ($SPTPath) {
        if (-not (Test-SptRoot $SPTPath)) {
            throw "'$SPTPath' is not an SPT install root -- no SPT_Runtime\SPT_Data under it."
        }

        return (Resolve-Path -LiteralPath $SPTPath).Path
    }

    $found = $null
    $how = $null

    if ($env:SPT_PATH -and (Test-SptRoot $env:SPT_PATH)) {
        $found = (Resolve-Path -LiteralPath $env:SPT_PATH).Path
        $how = 'SPT_PATH'
    }

    if (-not $found) {
        $found = Find-SptRootUpward $PSScriptRoot
        if ($found) { $how = 'found above this script' }
    }

    if (-not $found) {
        $found = Find-SptRootUpward (Get-Location).Path
        if ($found) { $how = 'found above the current directory' }
    }

    if (-not $found) {
        throw @'
Could not find an SPT install.

Point at one explicitly:
    -SPTPath <path to the folder holding EscapeFromTarkov.exe>

Or set it once for this shell:
    $env:SPT_PATH = '<path>'

Or run the script from inside an SPT install and it will find it by itself.
'@
    }

    if (-not $Quiet) {
        Write-Host "SPT install: $found ($how)" -ForegroundColor DarkGray
    }

    return $found
}
