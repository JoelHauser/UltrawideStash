<#
.SYNOPSIS
    Pack a profile's stash back into vanilla dimensions, without needing the mod.

.DESCRIPTION
    A standalone rescue for the one way Ultrawide Stash can leave you stuck: removing it
    while items are still sitting in the extra columns. Those items are never deleted --
    the SPT server has no out-of-bounds concept and never prunes -- but a vanilla stash is
    only 10 wide, so anything at column 10 or beyond becomes invisible.

    The mod does this automatically when you set columns to 10 and start the server once.
    This script exists for when that did not happen: it needs nothing but PowerShell, so it
    still works after the mod is gone, on a newer SPT, or years later.

    It reports by default and only writes when given -Apply. Every write is preceded by a
    timestamped .bak beside the profile.

    Run through PowerShell, not Bash -- the C:\HUH path mangling trap applies here too.

.PARAMETER SPTPath
    An SPT install root. Every profile under SPT_Runtime\user\profiles is examined.

.PARAMETER Profile
    A single profile .json instead of a whole install.

.PARAMETER Columns
    Target width. 10 is vanilla and is what you want for an uninstall.

.PARAMETER Apply
    Actually write. Without it, nothing is changed and you just get the report.

.EXAMPLE
    scripts\repair-stash.ps1 -SPTPath C:\HUH
    scripts\repair-stash.ps1 -SPTPath C:\HUH -Apply
    scripts\repair-stash.ps1 -Profile "C:\HUH\SPT_Runtime\user\profiles\abc.json" -Apply
#>
[CmdletBinding()]
param(
    [string] $SPTPath,
    [string] $Profile,
    [int]    $Columns = 10,
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'

# The five stashes a player can own, and their vanilla heights. Matches
# StashWidener.PlayerStashes; verified against items.json by test-database.ps1.
$VanillaRows = @{
    '566abbc34bdc2d92178b4576' = 30   # Standard
    '5811ce572459770cba1a34ea' = 40   # Left Behind
    '5811ce662459770f6f490f32' = 50   # Prepare for Escape
    '5811ce772459770e9e5f9532' = 68   # Edge of Darkness
    '6602bcf19cc643f44a04274b' = 72   # The Unheard Edition
}

# SortingTableWindow.ShowGrid calls SortingTable.ClampSize(7, 7).
$SortingTableColumns = 7

function Get-Profiles {
    if ($Profile) {
        if (-not (Test-Path $Profile)) { throw "No profile at '$Profile'." }
        return @($Profile)
    }

    if (-not $SPTPath) { throw "Pass -SPTPath or -Profile." }

    $dir = Join-Path $SPTPath 'SPT_Runtime\user\profiles'

    if (-not (Test-Path $dir)) { throw "No profiles folder at '$dir'." }

    return @(Get-ChildItem $dir -Filter '*.json' | Select-Object -ExpandProperty FullName)
}

# An item's footprint after rotation. ItemRotation is Horizontal = 0, Vertical = 1, and a
# rotated item swaps its two dimensions. Anything unrecognised is read as rotated, because
# that claims the larger vertical footprint and over-reporting is the safe direction.
function Get-Footprint {
    param($Location, [int] $Width, [int] $Height)

    $rotated = $false

    if ($null -ne $Location.r) {
        $r = $Location.r
        if ($r -is [string]) { $rotated = ($r -ne 'Horizontal' -and $r -ne '0') }
        else { $rotated = ([int]$r -ne 0) }
    }

    if ($rotated) { return @{ W = [Math]::Max(1, $Height); H = [Math]::Max(1, $Width) } }

    return @{ W = [Math]::Max(1, $Width); H = [Math]::Max(1, $Height) }
}

function Find-FreeSpace {
    param([bool[]] $Occupied, [int] $Cols, [int] $Rows, [int] $W, [int] $H)

    for ($y = 0; $y -le $Rows - $H; $y++) {
        for ($x = 0; $x -le $Cols - $W; $x++) {
            $free = $true

            for ($j = $y; $j -lt $y + $H -and $free; $j++) {
                for ($i = $x; $i -lt $x + $W -and $free; $i++) {
                    if ($Occupied[$j * $Cols + $i]) { $free = $false }
                }
            }

            if ($free) { return @{ X = $x; Y = $y } }
        }
    }

    return $null
}

function Set-Occupied {
    param([bool[]] $Occupied, [int] $Cols, [int] $X, [int] $Y, [int] $W, [int] $H)

    for ($j = $Y; $j -lt $Y + $H; $j++) {
        for ($i = $X; $i -lt $X + $W; $i++) {
            $Occupied[$j * $Cols + $i] = $true
        }
    }
}

# ConvertFrom-Json is safe here: a profile has no keys differing only by case, unlike the
# big SPT database tables that break PowerShell's parser.
function Repair-Profile {
    param([string] $Path)

    Write-Host ""
    Write-Host "== $(Split-Path $Path -Leaf)" -ForegroundColor Cyan

    $raw = Get-Content $Path -Raw -Encoding UTF8
    $json = $raw | ConvertFrom-Json

    $inventory = $json.characters.pmc.Inventory

    if (-not $inventory -or -not $inventory.stash) {
        Write-Host "   no PMC stash; skipped" -ForegroundColor DarkGray
        return
    }

    $stashId = $inventory.stash
    $tableId = $inventory.sortingTable

    $stashItem = $inventory.items | Where-Object { $_._id -eq $stashId } | Select-Object -First 1

    if (-not $stashItem) {
        Write-Host "   stash item not found; skipped" -ForegroundColor Yellow
        return
    }

    if (-not $VanillaRows.ContainsKey($stashItem._tpl)) {
        Write-Host "   unrecognised stash template $($stashItem._tpl); skipped" -ForegroundColor Yellow
        return
    }

    $rows = $VanillaRows[$stashItem._tpl]

    Write-Host "   target $Columns x $rows"

    $inStash = @($inventory.items | Where-Object {
        $_.parentId -eq $stashId -and $_.location -and $_.location.PSObject.Properties.Name -contains 'x'
    })

    $occupied = New-Object 'bool[]' ($Columns * $rows)
    $displaced = @()

    foreach ($item in $inStash) {
        # Every item's real size needs the database; without it, treat as 1x1. That
        # under-reports, which can only make the packing more conservative, never less.
        $fp = Get-Footprint -Location $item.location -Width 1 -Height 1

        $x = [int]$item.location.x
        $y = [int]$item.location.y

        if ($x -ge 0 -and $y -ge 0 -and ($x + $fp.W) -le $Columns -and ($y + $fp.H) -le $rows) {
            Set-Occupied $occupied $Columns $x $y $fp.W $fp.H
        }
        else {
            $displaced += [pscustomobject]@{ Item = $item; W = $fp.W; H = $fp.H }
        }
    }

    if ($displaced.Count -eq 0) {
        Write-Host "   nothing is out of bounds -- this profile is already fine" -ForegroundColor Green
        return
    }

    Write-Host "   $($displaced.Count) item(s) outside a $Columns-wide stash" -ForegroundColor Yellow

    $moved = 0
    $overflowed = 0
    $stuck = 0

    # Sorting table occupancy, for anything the stash cannot take back.
    $tableRows = 1
    $tableOcc = $null

    if ($tableId) {
        $inTable = @($inventory.items | Where-Object {
            $_.parentId -eq $tableId -and $_.location -and $_.location.PSObject.Properties.Name -contains 'x'
        })

        foreach ($t in $inTable) { $tableRows = [Math]::Max($tableRows, [int]$t.location.y + 1) }

        $tableRows += $displaced.Count
        $tableOcc = New-Object 'bool[]' ($SortingTableColumns * $tableRows)

        foreach ($t in $inTable) {
            $tx = [int]$t.location.x
            $ty = [int]$t.location.y
            if ($tx -lt $SortingTableColumns -and $ty -lt $tableRows) {
                Set-Occupied $tableOcc $SortingTableColumns $tx $ty 1 1
            }
        }
    }

    foreach ($d in ($displaced | Sort-Object { - ($_.W * $_.H) })) {
        $spot = Find-FreeSpace $occupied $Columns $rows $d.W $d.H

        if ($spot) {
            Set-Occupied $occupied $Columns $spot.X $spot.Y $d.W $d.H
            $d.Item.location.x = $spot.X
            $d.Item.location.y = $spot.Y
            $moved++
            continue
        }

        if ($tableOcc) {
            $spot = Find-FreeSpace $tableOcc $SortingTableColumns $tableRows $d.W $d.H

            if ($spot) {
                Set-Occupied $tableOcc $SortingTableColumns $spot.X $spot.Y $d.W $d.H
                $d.Item.parentId = $tableId
                $d.Item.slotId = 'hideout'
                $d.Item.location.x = $spot.X
                $d.Item.location.y = $spot.Y
                $overflowed++
                continue
            }
        }

        $stuck++
    }

    Write-Host "   $moved back into the stash, $overflowed into the sorting table, $stuck stuck"

    if ($stuck -gt 0) {
        Write-Host "   NOT writing this profile: $stuck item(s) had nowhere to go." -ForegroundColor Red
        return
    }

    if (-not $Apply) {
        Write-Host "   (report only -- re-run with -Apply to write)" -ForegroundColor DarkGray
        return
    }

    $backup = "$Path.ultrawidestash-repair-$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
    Copy-Item $Path $backup

    # UTF8Encoding($false): no BOM, which is what the server writes and expects.
    $out = $json | ConvertTo-Json -Depth 100 -Compress
    [System.IO.File]::WriteAllText($Path, $out, (New-Object System.Text.UTF8Encoding($false)))

    Write-Host "   written. Backup: $(Split-Path $backup -Leaf)" -ForegroundColor Green
}

Write-Host "Ultrawide Stash -- standalone stash repair" -ForegroundColor Cyan
Write-Host "Packing stashes back to $Columns columns." -ForegroundColor Cyan

if (-not $Apply) {
    Write-Host "Report only. Nothing will be written without -Apply." -ForegroundColor Yellow
}

$paths = Get-Profiles

foreach ($p in $paths) { Repair-Profile -Path $p }

Write-Host ""
Write-Host "Done. $($paths.Count) profile(s) examined." -ForegroundColor Cyan

if (-not $Apply) {
    Write-Host "Re-run with -Apply to make the changes." -ForegroundColor Yellow
}
