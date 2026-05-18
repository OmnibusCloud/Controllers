<#
.SYNOPSIS
    Pack Render's per-platform Blender + FFmpeg + benchmark prerequisites as
    GitHub Release artifacts and refresh ControllerDataAsset metadata in
    Render.csproj.

.DESCRIPTION
    Reads <Prerequisites>/blender/{windows-x64,linux-x64,macos-arm64}/, the
    same under ffmpeg/, and <Prerequisites>/benchmark/render/*.blend.

    Produces 6 zip archives (one per RID-x-tool) and 3 single-file .blend
    artifacts in <Output>. Computes SHA256 + size for each, prints a diff
    against current Render.csproj.

    Without -Apply: pure dry-run — does not touch the csproj.
    With -Apply: line-based rewrite of every ControllerDataAsset's <Sha256>,
    <Size>, and <Uri> (so the render-v<Version> release tag matches), plus
    the top-level <Version> bump. Preserves indentation, line endings, and
    surrounding XML formatting.

    Use case 1 (first cut):
      .\pack-render-prereqs.ps1 -Version 1.15.1 -Apply

    Use case 2 (quarterly Blender bump):
      Replace @Prerequisites/blender/<rid>/ trees with the new install,
      then run:
        .\pack-render-prereqs.ps1 -Version 1.15.2 -Apply -Clean
      Review the diff, gh release create, run Publish workflow.

.PARAMETER Version
    Render package version to publish, without 'v' prefix (e.g. '1.15.1').
    Drives the render-v<Version> GitHub Release tag embedded in every
    ControllerDataAsset Uri, and the <Version> field when -Apply is set.

.PARAMETER Prerequisites
    Path to the @Prerequisites root holding blender/, ffmpeg/, and
    benchmark/render/ subtrees. Defaults to the WitEngine sibling repo:
    <script-dir>\..\..\..\OutWit\WitEngine\@Prerequisites.

.PARAMETER Output
    Where to drop the 9 artifact files. Created if missing. Default is
    <Controllers>\dist\render-v<Version>\.

.PARAMETER Csproj
    Path to OutWit.Controller.Render.csproj. Default is the standard
    location under <Controllers>\Render\OutWit.Controller.Render\.

.PARAMETER Apply
    Rewrite Render.csproj in place. Without this switch the script is a
    pure dry-run that prints the diff and exits.

.PARAMETER Clean
    Wipe -Output before packing. Without this, an existing zip is left as-is
    (and re-hashed) — useful for incremental re-runs when only some
    platforms changed.

.EXAMPLE
    .\pack-render-prereqs.ps1 -Version 1.15.1

    Dry-run: pack everything fresh from the default @Prerequisites location,
    print the diff against Render.csproj. Does not modify any csproj.

.EXAMPLE
    .\pack-render-prereqs.ps1 -Version 1.15.1 -Apply

    Pack + write SHA / Size / Uri / Version into Render.csproj.

.EXAMPLE
    .\pack-render-prereqs.ps1 -Version 1.16.0 -Apply -Clean -Prerequisites D:\Mirror\@Prerequisites

    Force-clean output, pull prereqs from a custom location, apply for v1.16.0.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter()]
    [string]$Prerequisites,

    [Parameter()]
    [string]$Output,

    [Parameter()]
    [string]$Csproj,

    [switch]$Apply,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---- Defaults ---------------------------------------------------------------

if (-not $Prerequisites) {
    $Prerequisites = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\OutWit\WitEngine\@Prerequisites') -ErrorAction SilentlyContinue
    if (-not $Prerequisites) {
        $Prerequisites = Join-Path $PSScriptRoot '..\..\..\OutWit\WitEngine\@Prerequisites'
    }
}
if (-not $Output) {
    $Output = Join-Path $PSScriptRoot "..\dist\render-v$Version"
}
if (-not $Csproj) {
    $Csproj = Resolve-Path (Join-Path $PSScriptRoot '..\Render\OutWit.Controller.Render\OutWit.Controller.Render.csproj')
}

Write-Host "Render prereqs packer" -ForegroundColor Cyan
Write-Host "  Version       : $Version"
Write-Host "  Prerequisites : $Prerequisites"
Write-Host "  Output        : $Output"
Write-Host "  Csproj        : $Csproj"
Write-Host "  Apply         : $Apply"
Write-Host "  Clean         : $Clean"
Write-Host ""

if (-not (Test-Path $Prerequisites -PathType Container)) {
    throw "Prerequisites root not found: '$Prerequisites'"
}
if (-not (Test-Path $Csproj -PathType Leaf)) {
    throw "Csproj not found: '$Csproj'"
}

# ---- Artifact spec ----------------------------------------------------------
# Id matches <ControllerDataAsset Include="..."> in Render.csproj.
# ArtifactName is the filename on the GitHub Release.
# SourceKind: zip-folder = recursive zip of a directory; single-file = copy as-is.

$ARTIFACTS = @(
    [pscustomobject]@{ Id = 'blender-win-x64';        ArtifactName = 'blender-windows-x64.zip';     SourceKind = 'zip-folder';  SourcePath = 'blender/windows-x64' }
    [pscustomobject]@{ Id = 'blender-linux-x64';      ArtifactName = 'blender-linux-x64.zip';       SourceKind = 'zip-folder';  SourcePath = 'blender/linux-x64' }
    [pscustomobject]@{ Id = 'blender-osx-arm64';      ArtifactName = 'blender-macos-arm64.zip';     SourceKind = 'zip-folder';  SourcePath = 'blender/macos-arm64' }
    [pscustomobject]@{ Id = 'ffmpeg-win-x64';         ArtifactName = 'ffmpeg-windows-x64.zip';      SourceKind = 'zip-folder';  SourcePath = 'ffmpeg/windows-x64' }
    [pscustomobject]@{ Id = 'ffmpeg-linux-x64';       ArtifactName = 'ffmpeg-linux-x64.zip';        SourceKind = 'zip-folder';  SourcePath = 'ffmpeg/linux-x64' }
    [pscustomobject]@{ Id = 'ffmpeg-osx-arm64';       ArtifactName = 'ffmpeg-macos-arm64.zip';      SourceKind = 'zip-folder';  SourcePath = 'ffmpeg/macos-arm64' }
    [pscustomobject]@{ Id = 'benchmark-scene';        ArtifactName = 'benchmark_scene.blend';       SourceKind = 'single-file'; SourcePath = 'benchmark/render/benchmark_scene.blend' }
    [pscustomobject]@{ Id = 'benchmark-scene-still';  ArtifactName = 'benchmark_scene_still.blend'; SourceKind = 'single-file'; SourcePath = 'benchmark/render/benchmark_scene_still.blend' }
    [pscustomobject]@{ Id = 'benchmark-scene-video';  ArtifactName = 'benchmark_scene_video.blend'; SourceKind = 'single-file'; SourcePath = 'benchmark/render/benchmark_scene_video.blend' }
)

# ---- Pre-flight: every source must exist before we start ------------------

$missing = @()
foreach ($a in $ARTIFACTS) {
    $src = Join-Path $Prerequisites $a.SourcePath
    $needFolder = $a.SourceKind -eq 'zip-folder'
    $kind = if ($needFolder) { 'Container' } else { 'Leaf' }
    if (-not (Test-Path $src -PathType $kind)) {
        $missing += "$($a.Id): $src ($($a.SourceKind))"
    }
}
if ($missing.Count -gt 0) {
    Write-Host "Missing prerequisite sources:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    throw "Pre-flight failed. Populate -Prerequisites and re-run."
}

# ---- Stage artifacts ------------------------------------------------------

if ($Clean -and (Test-Path $Output)) {
    Write-Host "Cleaning $Output" -ForegroundColor DarkGray
    Remove-Item $Output -Recurse -Force
}
if (-not (Test-Path $Output)) {
    New-Item -ItemType Directory -Path $Output -Force | Out-Null
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($a in $ARTIFACTS) {
    $src = Join-Path $Prerequisites $a.SourcePath
    $dst = Join-Path $Output $a.ArtifactName

    if ($a.SourceKind -eq 'zip-folder') {
        if (Test-Path $dst) {
            Write-Host "  reuse  $($a.ArtifactName)" -ForegroundColor DarkGray
        } else {
            Write-Host "  pack   $($a.ArtifactName)  <-  $src" -ForegroundColor DarkCyan
            [System.IO.Compression.ZipFile]::CreateFromDirectory(
                $src, $dst,
                [System.IO.Compression.CompressionLevel]::Optimal,
                $false  # do NOT prepend source-dir name as zip root entry
            )
        }
    } else {
        if (Test-Path $dst) {
            Write-Host "  reuse  $($a.ArtifactName)" -ForegroundColor DarkGray
        } else {
            Write-Host "  copy   $($a.ArtifactName)  <-  $src" -ForegroundColor DarkCyan
            Copy-Item $src $dst -Force
        }
    }

    $sha = (Get-FileHash -Algorithm SHA256 $dst).Hash.ToLower()
    $size = (Get-Item $dst).Length
    $a | Add-Member -NotePropertyName 'NewSha256' -NotePropertyValue $sha -Force
    $a | Add-Member -NotePropertyName 'NewSize'   -NotePropertyValue $size -Force
}

# ---- Parse current csproj -------------------------------------------------

$csprojText = [System.IO.File]::ReadAllText($Csproj)

# Pull current Sha256/Size/Uri per asset block, using a regex that matches
# the ControllerDataAsset block bounded by its Include attribute.
$currentByEntry = @{}
foreach ($a in $ARTIFACTS) {
    $idEsc = [regex]::Escape($a.Id)
    $blockRegex = "(?s)<ControllerDataAsset Include=`"$idEsc`">(.+?)</ControllerDataAsset>"
    if ($csprojText -match $blockRegex) {
        $block = $matches[1]
        $oldSha  = if ($block -match '<Sha256>([^<]+)</Sha256>') { $matches[1] } else { '<missing>' }
        $oldSize = if ($block -match '<Size>([^<]+)</Size>')     { $matches[1] } else { '<missing>' }
        $oldUri  = if ($block -match '<Uri>([^<]+)</Uri>')        { $matches[1] } else { '<missing>' }
        $currentByEntry[$a.Id] = [pscustomobject]@{ Sha256 = $oldSha; Size = $oldSize; Uri = $oldUri }
    } else {
        $currentByEntry[$a.Id] = $null  # block doesn't exist
    }
}

# Current top-level <Version>
$currentVersion = if ($csprojText -match '<Version>([^<]+)</Version>') { $matches[1] } else { '<missing>' }

# ---- Print diff table -----------------------------------------------------

Write-Host ""
Write-Host "Render.csproj diff:" -ForegroundColor Cyan
Write-Host ""
Write-Host ("  Version:  {0,-12}  ->  {1}" -f $currentVersion, $Version)
Write-Host ""
Write-Host ("  {0,-25} {1,-10} {2,-10} {3,-12} {4,-12} {5}" -f 'Asset', 'Old SHA', 'New SHA', 'Old size', 'New size', 'Status')
Write-Host ("  " + ('-' * 95))

$dirty = $false
foreach ($a in $ARTIFACTS) {
    $cur = $currentByEntry[$a.Id]
    $newSha8 = $a.NewSha256.Substring(0, 8)
    if (-not $cur) {
        Write-Host ("  {0,-25} {1,-10} {2,-10} {3,-12} {4,-12} {5}" -f $a.Id, '<n/a>', $newSha8, '<n/a>', $a.NewSize, 'NEW') -ForegroundColor Yellow
        $dirty = $true
        continue
    }
    $oldSha8 = if ($cur.Sha256.Length -ge 8) { $cur.Sha256.Substring(0, 8) } else { $cur.Sha256 }
    $changed = $cur.Sha256 -ne $a.NewSha256
    if ($changed) { $dirty = $true }
    $statusColor = if ($changed) { 'Yellow' } else { 'DarkGray' }
    $statusText  = if ($changed) { 'CHANGED' } else { 'unchanged' }
    Write-Host ("  {0,-25} {1,-10} {2,-10} {3,-12} {4,-12} {5}" -f $a.Id, $oldSha8, $newSha8, $cur.Size, $a.NewSize, $statusText) -ForegroundColor $statusColor
}
Write-Host ""

# ---- Apply: rewrite csproj line-by-line -----------------------------------

if ($Apply) {
    $artifactsById = @{}
    foreach ($a in $ARTIFACTS) { $artifactsById[$a.Id] = $a }

    # Split keeping the line separators so output preserves CRLF/LF as-is.
    $parts = [regex]::Split($csprojText, '(\r?\n)')
    $sb = New-Object System.Text.StringBuilder
    $insideAssetId = $null
    $versionRewritten = $false

    for ($i = 0; $i -lt $parts.Length; $i++) {
        $line = $parts[$i]
        $newLine = $line

        # Track block entry/exit
        if ($line -match '<ControllerDataAsset Include="([^"]+)">') {
            $insideAssetId = $matches[1]
        }
        elseif ($line -match '</ControllerDataAsset>') {
            $insideAssetId = $null
        }
        # Inside a known block, rewrite the three mutable fields.
        elseif ($insideAssetId -and $artifactsById.ContainsKey($insideAssetId)) {
            $a = $artifactsById[$insideAssetId]
            if ($line -match '<Uri>[^<]+</Uri>') {
                $newUri = "gh-release://OmnibusCloud/Controllers/render-v$Version/$($a.ArtifactName)"
                $newLine = $line -replace '<Uri>[^<]+</Uri>', "<Uri>$newUri</Uri>"
            }
            elseif ($line -match '<Sha256>[^<]+</Sha256>') {
                $newLine = $line -replace '<Sha256>[^<]+</Sha256>', "<Sha256>$($a.NewSha256)</Sha256>"
            }
            elseif ($line -match '<Size>[^<]+</Size>') {
                $newLine = $line -replace '<Size>[^<]+</Size>', "<Size>$($a.NewSize)</Size>"
            }
        }
        # Top-level <Version> — rewrite the FIRST occurrence (the csproj's own
        # version, not any inner package-reference <Version> that might exist).
        elseif (-not $insideAssetId -and -not $versionRewritten -and $line -match '<Version>[^<]+</Version>') {
            $newLine = $line -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
            $versionRewritten = $true
        }

        [void]$sb.Append($newLine)
    }

    [System.IO.File]::WriteAllText($Csproj, $sb.ToString())
    Write-Host "Wrote: $Csproj" -ForegroundColor Green
}

# ---- Summary + next-step commands -----------------------------------------

Write-Host ""
Write-Host "Artifacts staged in: $Output" -ForegroundColor Cyan
Get-ChildItem $Output | ForEach-Object {
    $sizeMb = [math]::Round($_.Length / 1MB, 1)
    Write-Host ("  {0,-40} {1,8} MB" -f $_.Name, $sizeMb)
}

Write-Host ""
if (-not $Apply) {
    Write-Host "Dry-run only. To rewrite the csproj:" -ForegroundColor Yellow
    Write-Host "  pwsh -File '$PSCommandPath' -Version $Version -Apply"
    if ($Clean) { Write-Host "  (re-add -Clean if you want a fresh pack)" }
    Write-Host ""
}

Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1) git diff $Csproj                  # review changes"
Write-Host "  2) gh release create render-v$Version (Get-ChildItem '$Output\*' | ForEach-Object FullName) ``"
Write-Host "        --title 'OutWit.Controller.Render v$Version' ``"
Write-Host "        --notes 'Render Tier-2 assets: Blender, FFmpeg, benchmark scenes (win-x64 + linux-x64 + osx-arm64).'"
Write-Host "  3) git add + commit + push the csproj change"
Write-Host "  4) Trigger the Publish workflow for OutWit.Controller.Render"
Write-Host ""

if ($dirty -and -not $Apply) {
    exit 2   # non-zero so CI can detect "changes pending" if scripted
}
