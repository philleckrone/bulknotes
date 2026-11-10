<#
.SYNOPSIS
  Checks the plugin's tags against Paratext's own code. For each project it loads every Notes_*.xml
  with ParatextData's XmlSerializer for CommentList, groups the comments into CommentThread objects
  (sorted by date, as the plugin does) and asks ParatextData for each thread's TagIds and Type. Then
  it compares that with the test host's "list ... ALL --status all" output (column 10: tag ids, "C"
  before a conflict note). Read-only: nothing is written to the projects.

.EXAMPLE
  .\tags_oracle.ps1 -Projects "C:\My Paratext 8 Projects\PLe11;C:\My Paratext 8 Projects\ZTG"
  .\tags_oracle.ps1 -Root "C:\My Paratext 8 Projects"
#>
param(
    [string]$Projects = '',   # ';'-separated: powershell -File cannot pass arrays
    [string]$Root = '',
    [string]$ParatextDir = 'C:\Program Files\Paratext 9'
)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'TestHost\bin\Release\BulkNotesTestHost.exe'
$pt = [Reflection.Assembly]::LoadFrom((Join-Path $ParatextDir 'ParatextData.dll'))
$listType = $pt.GetType('Paratext.Data.ProjectComments.CommentList', $true)
$threadType = $pt.GetType('Paratext.Data.ProjectComments.CommentThread', $true)
$serializer = New-Object Xml.Serialization.XmlSerializer $listType
$folders = @($Projects -split ';' | Where-Object { $_ })
if ($Root) { $folders += @(Get-ChildItem -LiteralPath $Root -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'Settings.xml') } | ForEach-Object { $_.FullName }) }

$totalThreads = 0; $totalDiff = 0
foreach ($p in $folders) {
    $files = @(Get-ChildItem -LiteralPath $p -Filter 'Notes_*.xml' -File)
    if ($files.Count -eq 0) { continue }
    $comments = New-Object Collections.ArrayList
    foreach ($f in $files) {
        $reader = [Xml.XmlReader]::Create($f.FullName)
        try { $comments.AddRange(@($serializer.Deserialize($reader))) } finally { $reader.Close() }
    }
    # Paratext's tags and type for each thread
    $expected = @{}
    foreach ($g in ($comments | Where-Object { $_.Thread } | Group-Object { $_.Thread })) {
        $thread = [Activator]::CreateInstance($threadType)
        foreach ($c in ($g.Group | Sort-Object { $_.DateTime })) { $thread.Comments.Add($c) }
        $isConflict = $thread.Type.ToString() -match 'onflict'
        $expected[$g.Name] = $(if ($isConflict) { 'C' } else { '' }) + ((@($thread.TagIds) | ForEach-Object { $_ }) -join ',')
    }
    # The plugin's, through the test host (deleted notes are not listed)
    # The test host is a WinExe: "&" would neither wait for it nor capture its output.
    $psi = New-Object Diagnostics.ProcessStartInfo $exe
    $psi.Arguments = "list `"$p`" ALL --status all"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $proc = [Diagnostics.Process]::Start($psi)
    $out = $proc.StandardOutput.ReadToEnd() -split "`r?`n"
    $proc.WaitForExit()
    $diff = 0; $n = 0
    foreach ($line in $out) {
        $cols = $line -split "`t"
        if ($cols.Count -lt 10) { continue }
        $n++
        $want = $expected[$cols[0]]
        if ($want -ne $cols[9]) {
            $diff++
            if ($diff -le 5) { "  DIFF $(Split-Path $p -Leaf) $($cols[0]) $($cols[1]): plugin $($cols[9]), Paratext $want" }
        }
    }
    "{0}: {1} notes, {2} differences" -f (Split-Path $p -Leaf), $n, $diff
    $totalThreads += $n; $totalDiff += $diff
}
"TOTAL: $totalThreads notes, $totalDiff differences"
