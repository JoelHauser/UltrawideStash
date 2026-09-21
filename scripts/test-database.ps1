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

    Run through PowerShell, not Bash -- the C:\HUH path mangling trap that bites the
    sibling repos applies here too.

.EXAMPLE
    scripts\test-database.ps1 -SPTPath C:\HUH
#>
[CmdletBinding()]
param(
    [string] $SPTPath = 'C:\HUH'
)

$ErrorActionPreference = 'Stop'

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

# What the mod claims, mirrored from StashWidener.PlayerStashes and the _name fields.
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

Write-Host ''

if ($script:Failed -eq 0) {
    Write-Host "$($script:Passed) passed." -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Passed) passed, $($script:Failed) failed." -ForegroundColor Red
exit 1
