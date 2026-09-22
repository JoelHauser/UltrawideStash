<#
.SYNOPSIS
    Check the stash ids the server mod hard-codes against a real SPT database.

.DESCRIPTION
    StashWidener.PlayerStashes is a fixed list of five template ids. Nothing in the
    C# suite can tell whether those ids are real, because the suite never sees the
    database -- so this does, against an actual install.

    It asserts, for each of the five: the id exists in items.json, it is 10 cells
    wide (so the narrowing guard will not refuse it), and its height matches what
    the mod's documentation claims. It also checks that no OTHER stash-shaped item
    was missed.

    Run through PowerShell, not Bash -- a backslash path gets mangled otherwise, the
    sibling repos applies here too.

.EXAMPLE
    scripts\test-database.ps1
    scripts\test-database.ps1 -SPTPath D:\Games\SPT
#>
[CmdletBinding()]
param(
    [string] $SPTPath
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'SptPath.ps1')

$SPTPath = Resolve-SptPath -SPTPath $SPTPath

$script:Passed = 0
$script:Failed = 0

function Test-That {
    param([string] $Name, [scriptblock] $Body)

    try {
        $result = & $Body
        if ($result) {
            $script:Passed++
            Write-Host "  PASS  $Name" -ForegroundColor Green
        }
        else {
            $script:Failed++
            Write-Host "  FAIL  $Name" -ForegroundColor Red
        }
    }
    catch {
        $script:Failed++
        Write-Host "  FAIL  $Name -- $($_.Exception.Message)" -ForegroundColor Red
    }
}

$itemsPath = Join-Path $SPTPath 'SPT_Runtime\SPT_Data\database\templates\items.json'

if (-not (Test-Path $itemsPath)) {
    Write-Host "No items.json at $itemsPath" -ForegroundColor Red
    Write-Host "Pass -SPTPath pointing at an SPT install root."
    exit 2
}

Write-Host "Reading $itemsPath" -ForegroundColor Cyan

# ConvertFrom-Json cannot read the larger SPT tables -- keys differing only by case
# throw in PowerShell's case-insensitive parser. Nothing here needs a full parse, so
# the file is walked as lines instead, which is also far quicker on 18 MB.
$lines = [System.IO.File]::ReadAllLines($itemsPath)

# What the mod claims, mirrored from StashLadder.Rungs and the _name fields.
$expected = [ordered]@{
    '566abbc34bdc2d92178b4576' = @{ Edition = 'Standard';            Rows = 30 }
    '5811ce572459770cba1a34ea' = @{ Edition = 'Left Behind';         Rows = 40 }
    '5811ce662459770f6f490f32' = @{ Edition = 'Prepare for Escape';  Rows = 50 }
    '5811ce772459770e9e5f9532' = @{ Edition = 'Edge of Darkness';    Rows = 68 }
    '6602bcf19cc643f44a04274b' = @{ Edition = 'The Unheard Edition'; Rows = 72 }
}

# Walk once, recording the id that owns each line, then read the grid off each
# stash's block. An item's block starts at indent 2; its grid props are deeper.
$found = @{}
$currentId = $null

for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]

    if ($line -match '^\s{2}"([0-9a-f]{24})":\s*\{') {
        $currentId = $matches[1]
        continue
    }

    if ($null -eq $currentId) { continue }
    if (-not $expected.Contains($currentId)) { continue }

    if ($line -match '"cellsH":\s*(\d+)') {
        if (-not $found.ContainsKey($currentId)) { $found[$currentId] = @{} }
        if (-not $found[$currentId].ContainsKey('CellsH')) {
            $found[$currentId].CellsH = [int]$matches[1]
        }
    }

    if ($line -match '"cellsV":\s*(\d+)') {
        if (-not $found.ContainsKey($currentId)) { $found[$currentId] = @{} }
        if (-not $found[$currentId].ContainsKey('CellsV')) {
            $found[$currentId].CellsV = [int]$matches[1]
        }
    }
}

Write-Host ''
Write-Host 'The five stash templates the mod edits:' -ForegroundColor Cyan

foreach ($id in $expected.Keys) {
    $edition = $expected[$id].Edition
    $rows = $expected[$id].Rows

    Test-That "$edition ($id) exists in items.json" {
        $found.ContainsKey($id)
    }

    Test-That "$edition is 10 cells wide" {
        $found.ContainsKey($id) -and $found[$id].CellsH -eq 10
    }

    Test-That "$edition is $rows cells tall" {
        $found.ContainsKey($id) -and $found[$id].CellsV -eq $rows
    }
}

# A stash the mod does not know about would simply stay at 10 wide while the others
# widened, which is the kind of fault that shows up as "my stash did not change" on
# one account edition only. Catch it here instead.
Write-Host ''
Write-Host 'No player stash is missing from the list:' -ForegroundColor Cyan

$namedStashes = @{}
$currentId = $null

for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^\s{2}"([0-9a-f]{24})":\s*\{') {
        $currentId = $matches[1]
        continue
    }

    # The player stashes are the ones BSG named "<edition> stash 10x<n>". The
    # hideout and scav containers are named differently and are not this mod's
    # business.
    if ($lines[$i] -match '"_name":\s*"(.*stash 10x\d+)"') {
        $namedStashes[$currentId] = $matches[1]
    }
}

Test-That "every '... stash 10xN' item is either handled or deliberately excluded" {
    $unhandled = @()

    foreach ($id in $namedStashes.Keys) {
        if ($expected.Contains($id)) { continue }

        # The developer stash is excluded on purpose: 10x300, not a thing a player
        # owns, and widening it would be meaningless.
        if ($id -eq '5c0a596086f7747bef5731c2') { continue }

        $unhandled += "$id ($($namedStashes[$id]))"
    }

    if ($unhandled.Count -gt 0) {
        Write-Host "        unhandled: $($unhandled -join ', ')" -ForegroundColor Yellow
        return $false
    }

    return $true
}

# ---- the hideout stash ladder ------------------------------------------------
#
# Not everybody plays Edge of Darkness. A Standard player upgrades the hideout's
# Stash area and the game moves them onto a different stash template -- the area's
# StashSize bonus carries a templateId, not a row count, and its value is 0.
#
# StashLadder.Rungs has to be in that order, or the row floor it computes does not
# cover the rung a player is about to arrive on and an upgrade strands their items.
# So the order is checked against the database rather than trusted.

$areasPath = Join-Path $SPTPath 'SPT_Runtime\SPT_Data\database\hideout\areas.json'

Write-Host ''
Write-Host "Reading $areasPath" -ForegroundColor Cyan

# The ladder as StashLadder.cs declares it, first four rungs -- the hideout only
# climbs as far as Edge of Darkness; The Unheard Edition is edition-only.
$ladder = @(
    '566abbc34bdc2d92178b4576',
    '5811ce572459770cba1a34ea',
    '5811ce662459770f6f490f32',
    '5811ce772459770e9e5f9532'
)

function Get-StashArea {
    param([string] $Path)

    Add-Type -AssemblyName System.Web.Extensions
    $ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $ser.MaxJsonLength = [int]::MaxValue

    $areas = $ser.DeserializeObject((Get-Content $Path -Raw))

    return $areas | Where-Object { $_.type -eq 3 }
}

Test-That "the hideout Stash area upgrades through the templates the mod edits, in order" {
    if (-not (Test-Path $areasPath)) {
        Write-Host "        no areas.json at $areasPath" -ForegroundColor Yellow
        return $false
    }

    $area = Get-StashArea -Path $areasPath

    if (-not $area) {
        Write-Host '        no area of type 3 (Stash) found' -ForegroundColor Yellow
        return $false
    }

    if ($area._id -ne '5d484fc0654e76006657e0ab') {
        Write-Host "        Stash area id is $($area._id), not the documented one" -ForegroundColor Yellow
        return $false
    }

    $stages = @()

    foreach ($key in ($area.stages.Keys | Sort-Object { [int] $_ })) {
        foreach ($bonus in $area.stages[$key].bonuses) {
            if ($bonus.type -eq 'StashSize' -and $bonus.templateId) {
                $stages += $bonus.templateId
            }
        }
    }

    if ($stages.Count -ne $ladder.Count) {
        Write-Host "        $($stages.Count) StashSize stages, expected $($ladder.Count)" -ForegroundColor Yellow
        Write-Host "        found: $($stages -join ', ')" -ForegroundColor Yellow
        return $false
    }

    for ($i = 0; $i -lt $ladder.Count; $i++) {
        if ($stages[$i] -ne $ladder[$i]) {
            Write-Host "        stage $($i + 1) is $($stages[$i]), expected $($ladder[$i])" -ForegroundColor Yellow
            return $false
        }
    }

    return $true
}

Test-That "every hideout stash stage points at a template the mod actually widens" {
    if (-not (Test-Path $areasPath)) { return $false }

    $area = Get-StashArea -Path $areasPath

    foreach ($key in $area.stages.Keys) {
        foreach ($bonus in $area.stages[$key].bonuses) {
            if ($bonus.type -ne 'StashSize' -or -not $bonus.templateId) { continue }

            # A stage pointing somewhere the mod does not edit means a player would
            # upgrade into a vanilla 10-wide stash and strand everything past x=9.
            if (-not $expected.Contains($bonus.templateId)) {
                Write-Host "        stage $key -> $($bonus.templateId), which the mod does not widen" -ForegroundColor Yellow
                return $false
            }
        }
    }

    return $true
}

Test-That "the hideout stash ladder rises in capacity, so no upgrade is a downgrade" {
    $previous = 0

    foreach ($id in $ladder) {
        $rows = $expected[$id].Rows

        if ($rows -lt $previous) {
            Write-Host "        $id has $rows rows, fewer than the $previous below it" -ForegroundColor Yellow
            return $false
        }

        $previous = $rows
    }

    return $true
}

Write-Host ''

if ($script:Failed -eq 0) {
    Write-Host "$($script:Passed) passed." -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Passed) passed, $($script:Failed) failed." -ForegroundColor Red
exit 1
