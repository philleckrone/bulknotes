<#
.SYNOPSIS
  UI Automation test of the Bulk Note Editing window, run through the test host on a copy of a
  project's Settings.xml plus tests\fixtures\Notes_*.xml (notes in MRK 1-3). Scenarios:
    1. Opens at MRK 2 (chapter scope): 5 unresolved notes, a range note shown as MRK 2:10-12.
    2. Filters: status All, created by, comment by, assigned to Team, words, Clear filters.
    3. Sorting by a column header, both directions.
    4. Scopes: Verse (verse 11 from Paratext, Next by verse), Whole book.
    5. Two notes ticked and resolved: comments with Status done are written, the list shrinks.
    6. All listed notes assigned to a user with a comment.
    7. Go to in Paratext asks for the verse.
    8. The scope is remembered; BN_MOVE: the window follows Paratext to MRK 3.
    9. BN_NOLOCK (observer): a warning, and Resolve/Assign are off.
   10. Read (1.4), on a fresh copy with tests\fixtures\ThreadStatus_Test User.ths beside it:
       Resolved + Unread lists only MRK 2:20; the Read column; unread rows are bold.
   11. Several projects (1.4): TST2 ticked in the Projects dialog; the Project column, both
       projects' notes, a tag by name across them, Assign skipping a project where the person is
       not active, and the choice remembered.
   12. Mark as read (1.5): Unread + Status All, Tick all, "Mark ticked as read", Yes: the list
       empties, no comment is written, the read-status file gets the notes; an observer can too.
       Then (1.6) Tick all, "Mark ticked as unread", Yes: all 7 unread again.
  The mouse is never moved: clicks and choices are posted to the test window only.

.EXAMPLE
  .\ui_smoke.ps1 -Project "C:\ClaudeCode Projects\TestProject\PDT15" -Out C:\temp\bn -Lang es
#>
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Lang = 'en',
    [string]$ExePath = ''
)
$ErrorActionPreference = 'Stop'
if (-not $ExePath) { $ExePath = Join-Path $PSScriptRoot 'TestHost\bin\Release\BulkNotesTestHost.exe' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class BnWin {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct Pt { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref Pt p);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
    const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, BM_CLICK = 0xF5, WM_COMMAND = 0x111;
    const int CB_GETCOUNT = 0x146, CB_GETLBTEXT = 0x148, CB_GETLBTEXTLEN = 0x149, CB_SETCURSEL = 0x14E, CB_GETCURSEL = 0x147;
    public static void Click(IntPtr h, int sx, int sy) {
        Pt p; p.X = sx; p.Y = sy;
        ScreenToClient(h, ref p);
        IntPtr lp = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
        PostMessage(h, WM_LBUTTONDOWN, (IntPtr)1, lp);
        PostMessage(h, WM_LBUTTONUP, IntPtr.Zero, lp);
    }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out Rect r);
    /// <summary>Clicks at a point given as a fraction of the window's client area, so the
    /// position is right whatever the DPI of the monitor the window is on.</summary>
    public static void ClickAt(IntPtr h, double fx, double fy) {
        Rect c; GetClientRect(h, out c);
        int x = (int)(fx * c.R), y = (int)(fy * c.B);
        IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
        PostMessage(h, WM_LBUTTONDOWN, (IntPtr)1, lp);
        PostMessage(h, WM_LBUTTONUP, IntPtr.Zero, lp);
    }
    const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101;
    /// <summary>Posts a key press (e.g. 0x28 = Down) to a window, without the keyboard focus moving.</summary>
    public static void PostKey(IntPtr h, int vk) {
        PostMessage(h, WM_KEYDOWN, (IntPtr)vk, (IntPtr)1);
        PostMessage(h, WM_KEYUP, (IntPtr)vk, (IntPtr)unchecked((int)0xC0000001));
    }
    public static void PressButton(IntPtr h) { PostMessage(h, BM_CLICK, IntPtr.Zero, IntPtr.Zero); }
    public static string ComboItem(IntPtr h, int i) {
        int n = (int)SendMessage(h, CB_GETLBTEXTLEN, (IntPtr)i, IntPtr.Zero);
        var sb = new StringBuilder(n + 1);
        SendMessage(h, CB_GETLBTEXT, (IntPtr)i, sb);
        return sb.ToString();
    }
    public static int ComboCount(IntPtr h) { return (int)SendMessage(h, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero); }
    public static int ComboSel(IntPtr h) { return (int)SendMessage(h, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); }
    /// <summary>Selects an item and tells the window, as a user's choice would (CBN_SELCHANGE).</summary>
    public static void ComboSelect(IntPtr h, int i) {
        SendMessage(h, CB_SETCURSEL, (IntPtr)i, IntPtr.Zero);
        int id = GetDlgCtrlID(h);
        SendMessage(GetParent(h), WM_COMMAND, (IntPtr)((1 << 16) | (id & 0xFFFF)), h);
    }
    const int WM_SETTEXT = 0x0C, WM_GETTEXT = 0x0D, WM_GETTEXTLENGTH = 0x0E;
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] static extern IntPtr SendText(IntPtr h, int msg, IntPtr w, string l);
    /// <summary>Sets an EDIT box's text; WinForms raises TextChanged as for typing.</summary>
    public static void SetText(IntPtr h, string text) { SendText(h, WM_SETTEXT, IntPtr.Zero, text); }
    public static string GetText(IntPtr h) {
        int n = (int)SendMessage(h, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        var sb = new StringBuilder(n + 1);
        SendMessage(h, WM_GETTEXT, (IntPtr)(n + 1), sb);
        return sb.ToString();
    }
    const int LB_SETCURSEL = 0x186, LB_GETCOUNT = 0x18B, LB_GETITEMRECT = 0x198;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref Rect r);
    /// <summary>A click posted on a list-box item (a CheckedListBox with CheckOnClick ticks it).</summary>
    public static void ListClickItem(IntPtr h, int i) {
        Rect r = new Rect();
        SendMessage(h, LB_GETITEMRECT, (IntPtr)i, ref r);
        IntPtr lp = (IntPtr)((((r.T + r.B) / 2) << 16) | ((r.L + 8) & 0xFFFF));
        PostMessage(h, WM_LBUTTONDOWN, (IntPtr)1, lp);
        PostMessage(h, WM_LBUTTONUP, IntPtr.Zero, lp);
    }
    public static int ListCount(IntPtr h) { return (int)SendMessage(h, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero); }
    /// <summary>Selects a list-box item and tells the window (LBN_SELCHANGE).</summary>
    public static void ListSelect(IntPtr h, int i) {
        SendMessage(h, LB_SETCURSEL, (IntPtr)i, IntPtr.Zero);
        int id = GetDlgCtrlID(h);
        SendMessage(GetParent(h), WM_COMMAND, (IntPtr)((1 << 16) | (id & 0xFFFF)), h);
    }
}
'@
[BnWin]::SetProcessDPIAware() | Out-Null
$AE = [Windows.Automation.AutomationElement]
$Scope = [Windows.Automation.TreeScope]
$CT = [Windows.Automation.ControlType]
$root = $AE::RootElement
$failures = 0
function Check($name, [bool]$ok, $detail = '') {
    if ($ok) { "PASS $name" } else { "FAIL $name  $detail"; $script:failures++ }
}

$es = $Lang -like 'es*'; $fr = $Lang -like 'fr*'
function T($en, $sp, $fra) { if ($es) { $sp } elseif ($fr) { $fra } else { $en } }
$colTick = T 'Pick' 'Marcar' 'Cocher'
$colRef = T 'Reference' 'Referencia' "R$([char]0xE9)f$([char]0xE9)rence"
$colCreatedBy = T 'Created by' 'Creada por' "Cr$([char]0xE9)$([char]0xE9)e par"
$colAssigned = T 'Assigned to' 'Asignada a' "Attribu$([char]0xE9)e $([char]0xE0)"
$title = T 'Bulk Note Editing' "Edici$([char]0xF3)n de notas en lote" 'Modification de notes en lot'
$goToText = T '^Go to in Paratext$' '^Ir al vers.culo en Paratext$' '^Aller au verset dans Paratext$'
$clearText = T '^Clear filters$' '^Quitar filtros$' '^Enlever les filtres$'
$resolveText = T '^Resolve ticked \(' '^Resolver marcadas \(' "^R$([char]0xE9)soudre les coch$([char]0xE9)es \("
$markReadText = T '^Mark ticked as read \(' "^Marcar las marcadas como le$([char]0xED)das \(" "^Marquer les coch$([char]0xE9)es comme lues \("
$markUnreadText = T '^Mark ticked as unread \(' "^Marcar las marcadas como no le$([char]0xED)das \(" "^Marquer les coch$([char]0xE9)es comme non lues \("
$tickAllText = T '^Tick all listed$' '^Marcar todas las mostradas$' '^Cocher toutes les notes affich'

# ------------------------------------------------------------ the project copy
New-Item -ItemType Directory -Force $Out | Out-Null
$proj = Join-Path $Out 'FIX'
$proj2 = Join-Path $Out 'TST2'
$readStatus = Join-Path $Out 'ThreadStatus_Test User.ths'
function FreshProjects {
    foreach ($d in @($proj, $proj2)) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
    New-Item -ItemType Directory -Force $proj | Out-Null
    Copy-Item -LiteralPath (Join-Path $Project 'Settings.xml') -Destination $proj
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'fixtures') -Filter 'Notes_*.xml' | Copy-Item -Destination $proj
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures\ProjectUserAccess.xml') -Destination $proj
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures\commentTags.xml') -Destination $proj
    # TST2: the second fixture project, with its own Guid.
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures\TST2') -Destination $proj2 -Recurse
    $xml = [IO.File]::ReadAllText((Join-Path $Project 'Settings.xml'))
    $xml = [regex]::Replace($xml, '<Guid>[^<]*</Guid>', '<Guid>tst2000000000000000000000000000000000002</Guid>')
    [IO.File]::WriteAllText((Join-Path $proj2 'Settings.xml'), $xml)
}
FreshProjects
# Scenarios 1-9 run without a read-status file (every note unread); 10 and 11 with the fixture's.
if (Test-Path $readStatus) { Remove-Item $readStatus }
$settings = Join-Path $Out 'settings.xml'
if (Test-Path $settings) { Remove-Item $settings }

# ------------------------------------------------------------ helpers
$script:proc = $null
$script:win = $null
# On any error, close the test window too, so a failed step never leaves it open (and the run waiting).
trap { if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill() }; "ERROR $_"; "$failures failure(s) before the error"; break }
function Launch($extraEnv, $extraArgs = '') {
    $psi = New-Object Diagnostics.ProcessStartInfo $ExePath
    $psi.Arguments = "form `"$proj`" $extraArgs --lang $Lang --ref MRK 2 11"
    $psi.EnvironmentVariables['BN_SETTINGS'] = $settings
    foreach ($k in @('BN_MOVE', 'BN_NOLOCK', 'BN_USERS')) { $psi.EnvironmentVariables.Remove($k) }
    $psi.EnvironmentVariables['BN_USERS'] = 'Rosa Admin'
    # Open the window on a screen without the mouse cursor (its hover effects disturb posted clicks);
    # the mouse itself is never moved.
    Add-Type -AssemblyName System.Windows.Forms
    $cursor = [Windows.Forms.Cursor]::Position
    $screen = [Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Bounds.Contains($cursor) } | Select-Object -First 1
    if ($screen) { $psi.EnvironmentVariables['BN_LOCATION'] = "$($screen.WorkingArea.X + 10),$($screen.WorkingArea.Y + 10)" }
    if ($extraEnv) { foreach ($k in $extraEnv.Keys) { $psi.EnvironmentVariables[$k] = $extraEnv[$k] } }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $script:proc = [Diagnostics.Process]::Start($psi)
    $byProcess = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
    $script:win = $null
    for ($i = 0; $i -lt 100 -and -not $script:win; $i++) {
        Start-Sleep -Milliseconds 200
        $script:win = $root.FindFirst($Scope::Children, $byProcess)
    }
    if (-not $script:win) { throw 'window did not appear' }
    Start-Sleep -Milliseconds 2000
}
function CloseWindow {
    $script:win.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    $script:proc.WaitForExit(10000) | Out-Null
    $script:proc.StandardOutput.ReadToEnd()
}
# The line beside "Hide filters" (a Text element starting with "Filters:").
function SummaryText {
    $prefix = T '^Filters:' '^Filtros:' '^Filtres :'
    (All | Where-Object { $_.Current.ControlType -eq $CT::Text -and $_.Current.Name -match $prefix } | Select-Object -First 1).Current.Name
}
# The background colour of a cell, from a PrintWindow capture (right edge of the cell, past its text).
function CellColor($header, $row) {
    $cell = Cell $header $row $CT::Edit
    $cr = $cell.Current.BoundingRectangle
    $h = [IntPtr]$script:win.Current.NativeWindowHandle
    $r = New-Object BnWin+Rect
    [BnWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [BnWin]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    # UIA and the capture can use different scales on a monitor with another DPI: go by fractions of the window.
    $wr = $script:win.Current.BoundingRectangle
    $x = [int](($cr.X + $cr.Width - 6 - $wr.X) / $wr.Width * $bmp.Width)
    $y = [int](($cr.Y + $cr.Height / 2 - $wr.Y) / $wr.Height * $bmp.Height)
    $px = $bmp.GetPixel($x, $y)
    $g.Dispose(); $bmp.Dispose()
    '{0:X2}{1:X2}{2:X2}' -f $px.R, $px.G, $px.B
}
function Shot($name) {
    $h = [IntPtr]$script:win.Current.NativeWindowHandle
    $r = New-Object BnWin+Rect
    [BnWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [BnWin]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $bmp.Save((Join-Path $Out $name), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "SHOT $name"
}
function All { @($script:win.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) }
function FindByText($pattern) {
    All | Where-Object { $_.Current.Name -match $pattern -and $_.Current.Name -notmatch ' Row ' } | Select-Object -First 1
}
function Press($pattern) {
    $el = FindByText $pattern
    if (-not $el) { throw "button $pattern not found" }
    [BnWin]::PressButton([IntPtr]$el.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 700
}
function Combo($name) {
    $c1 = New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, $name)
    $c2 = New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $CT::ComboBox)
    $el = $script:win.FindFirst($Scope::Descendants, (New-Object Windows.Automation.AndCondition $c1, $c2))
    if (-not $el) { throw "combo $name not found" }
    [IntPtr]$el.Current.NativeWindowHandle
}
function ComboText($name) { $h = Combo $name; $i = [BnWin]::ComboSel($h); if ($i -ge 0) { [BnWin]::ComboItem($h, $i) } else { '' } }
function Choose($name, $itemPattern) {
    $h = Combo $name
    for ($i = 0; $i -lt [BnWin]::ComboCount($h); $i++) {
        if ([BnWin]::ComboItem($h, $i) -match $itemPattern) { [BnWin]::ComboSelect($h, $i); Start-Sleep -Milliseconds 700; return }
    }
    throw "$itemPattern not in $name"
}
function ChooseIndex($name, $i) { [BnWin]::ComboSelect((Combo $name), $i); Start-Sleep -Milliseconds 700 }
# Sortable columns add ", Not sorted." (or the sort order) to each cell's name.
function Cell($header, $row, $type) {
    $c2 = New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $type)
    $want = '^' + [regex]::Escape("$header Row $row") + '(,|$)'
    @($script:win.FindAll($Scope::Descendants, $c2)) | Where-Object { $_.Current.Name -match $want } | Select-Object -First 1
}
function CellValue($el) { $el.GetCurrentPropertyValue([Windows.Automation.ValuePattern]::ValueProperty) }
function Refs {
    $refs = @()
    for ($r = 0; $r -lt 200; $r++) {
        $c = Cell $colRef $r $CT::Edit
        if (-not $c) { break }
        $refs += (CellValue $c)
    }
    , $refs
}
function Tick($row) {
    (Cell $colTick $row $CT::CheckBox).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 300
}
function CountsText {
    (All | Where-Object { $_.Current.ControlType -eq $CT::Text -and $_.Current.Name -match '^(MRK|FIX)' } | Select-Object -First 1).Current.Name
}
# "MRK 2: 7 note(s) read, 5 listed, 0 ticked." -> read, listed, ticked
function Counts { if ((CountsText) -match ': (\d+)\D+(\d+)\D+(\d+)') { @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3]) } else { @(-1, -1, -1) } }
# WinForms text boxes reach UI Automation as unnamed panes: they are found by window class,
# top to bottom: the Words filter, the comment to add, the results.
function Edit($name) {
    $boxes = @(All | Where-Object { $_.Current.ClassName -match '\.EDIT\.' } | Sort-Object { $_.Current.BoundingRectangle.Y })
    $i = @{ 'Words' = 0; 'Comment' = 1; 'Results' = 2 }[$name]
    [IntPtr]$boxes[$i].Current.NativeWindowHandle
}
function SetText($name, $text) {
    [BnWin]::SetText((Edit $name), $text)
    Start-Sleep -Milliseconds 900
}
function GetText($name) { [BnWin]::GetText((Edit $name)) }
function Results { GetText 'Results' }
function AnswerYes {
    $byTitle = New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, $title)
    $dialog = $null
    for ($i = 0; $i -lt 30 -and -not $dialog; $i++) {
        Start-Sleep -Milliseconds 200
        $dialog = $root.FindFirst($Scope::Children, (New-Object Windows.Automation.AndCondition (New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)), $byTitle))
        if (-not $dialog) { $dialog = $script:win.FindFirst($Scope::Children, $byTitle) }
    }
    if (-not $dialog) { throw 'confirmation dialog did not appear' }
    $text = (@($dialog.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) |
        ForEach-Object { $_.Current.Name }) -join ' '
    $yes = @($dialog.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) |
        Where-Object { $_.Current.Name -match "^&?(Yes|S$([char]0xED)|Oui)$" } | Select-Object -First 1
    [BnWin]::PressButton([IntPtr]$yes.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 1500
    $text
}
function Written {
    $f = Join-Path $proj 'Notes_Test User.xml'
    if (-not (Test-Path $f)) { return @() }
    @(([xml](Get-Content -LiteralPath $f -Raw -Encoding UTF8)).CommentList.Comment)
}

# ------------------------------------------------------------ 1. a chapter, unresolved notes
Launch $null
Shot '1_start.png'
Check 'starts at MRK 2, chapter scope' ((ComboText 'Book') -eq 'MRK' -and (ComboText 'Chapter') -eq '2' -and (ComboText 'Scope') -eq (T 'Chapter' "Cap$([char]0xED)tulo" 'Chapitre')) "$(ComboText 'Book') $(ComboText 'Chapter') $(ComboText 'Scope')"
$c = Counts
Check 'MRK 2: 5 unresolved notes' ($c[0] -eq 5 -and $c[1] -eq 5) (CountsText)
$refs = Refs
Check 'range shown as MRK 2:10-12' ($refs -contains 'MRK 2:10-12') ($refs -join ', ')

$h = Combo 'AssignTo'
$people = @(0..([BnWin]::ComboCount($h) - 1) | ForEach-Object { [BnWin]::ComboItem($h, $_) })
Check 'assign to: Team, the active users, nobody' (($people -join '|') -eq ((T 'Team' 'Equipo' "$([char]0xC9)quipe") + '|Ana Consultora|Luis Revisor|Rosa Admin|Olga Observadora|' + (T '(nobody)' '(nadie)' '(personne)'))) ($people -join '|')

# ------------------------------------------------------------ 2. filters
ChooseIndex 'Status' 2
$c = Counts
Check 'status All reads the resolved notes too' ($c[0] -eq 7 -and $c[1] -eq 7) (CountsText)
Choose 'CreatedBy' '^Luis Revisor$'
$c = Counts
Check 'created by Luis: 2' ($c[1] -eq 2) (CountsText)
Choose 'CommentedBy' '^Ana Consultora$'
$c = Counts
Check 'and a comment by Ana: 1 (MRK 2:5)' ($c[1] -eq 1 -and (Refs)[0] -eq 'MRK 2:5') (CountsText)
Press $clearText
$c = Counts
Check 'clear filters: 7 again' ($c[1] -eq 7) (CountsText)
Choose 'AssignedTo' (T '^Team$' '^Equipo$' "^$([char]0xC9)quipe$")
Check 'assigned to Team: 1' ((Counts)[1] -eq 1 -and (CellValue (Cell $colAssigned 0 $CT::Edit)) -eq (T 'Team' 'Equipo' "$([char]0xC9)quipe")) (CountsText)
Choose 'AssignedTo' '^\('
Check 'assigned to anyone: 7' ((Counts)[1] -eq 7) (CountsText)
SetText 'Words' 'cuatro hombres'
Check 'words: 1 (in a later comment)' ((Counts)[1] -eq 1 -and (Refs)[0] -eq 'MRK 2:3') (CountsText)
Shot '2_filtered.png'
Press $clearText
Choose 'Tag' '^Preguntas$'
$tagsCol = T 'Tags' 'Etiquetas' "$([char]0xC9)tiquettes"
$r4 = [array]::IndexOf((Refs), 'MRK 2:5')
Check 'tag filter Preguntas: 2 notes, both tags shown' ((Counts)[1] -eq 2 -and (CellValue (Cell $tagsCol $r4 $CT::Edit)) -eq 'Claridad, Preguntas') "$(CountsText) / $(if ($r4 -ge 0) { CellValue (Cell $tagsCol $r4 $CT::Edit) })"
$h = Combo 'Tag'
$tagItems = @(0..([BnWin]::ComboCount($h) - 1) | ForEach-Object { [BnWin]::ComboItem($h, $_) })
Check 'tag list: the project tags, then the built-in ones' ($tagItems.Count -eq 7 -and $tagItems[1] -eq 'To Do' -and $tagItems[3] -eq 'Claridad') ($tagItems -join '|')
Press $clearText

# ------------------------------------------------------------ 3. sorting
$header = All | Where-Object { $_.Current.ControlType -eq $CT::Header -and $_.Current.Name -eq $colCreatedBy } | Select-Object -First 1
$hr = $header.Current.BoundingRectangle
$gridEl = All | Where-Object { $_.Current.ControlType -eq $CT::Table } | Select-Object -First 1
$gr = $gridEl.Current.BoundingRectangle
[BnWin]::ClickAt([IntPtr]$gridEl.Current.NativeWindowHandle, ($hr.X + $hr.Width / 2 - $gr.X) / $gr.Width, ($hr.Y + $hr.Height / 2 - $gr.Y) / $gr.Height)
Start-Sleep -Milliseconds 700
$first = CellValue (Cell $colCreatedBy 0 $CT::Edit)
$last = CellValue (Cell $colCreatedBy 6 $CT::Edit)
Check 'sorted by Created by' ($first -eq 'Ana Consultora' -and $last -eq 'Luis Revisor') "$first .. $last"
[BnWin]::ClickAt([IntPtr]$gridEl.Current.NativeWindowHandle, ($hr.X + $hr.Width / 2 - $gr.X) / $gr.Width, ($hr.Y + $hr.Height / 2 - $gr.Y) / $gr.Height)
Start-Sleep -Milliseconds 700
Check 'sorted by Created by, descending' ((CellValue (Cell $colCreatedBy 0 $CT::Edit)) -eq 'Luis Revisor') ''

# ------------------------------------------------------------ 4. scopes
ChooseIndex 'Scope' 0
Check 'verse scope: verse 11 from Paratext, range note listed' ((ComboText 'Verse') -eq '11' -and (Counts)[1] -eq 1 -and (Refs)[0] -eq 'MRK 2:10-12') (CountsText)
Press '>$'
Check 'Next in verse scope: verse 12' ((ComboText 'Verse') -eq '12' -and (Counts)[1] -eq 1) (CountsText)
Press '>$'
Check 'verse 13: nothing' ((Counts)[1] -eq 0) (CountsText)
ChooseIndex 'Scope' 2
$c = Counts
Check 'whole book: 9 notes' ($c[0] -eq 9 -and (CountsText) -match '^MRK ?:') (CountsText)
Shot '4_book.png'
ChooseIndex 'Scope' 3
$c = Counts
$bookBox = All | Where-Object { $_.Current.ControlType -eq $CT::ComboBox -and $_.Current.Name -eq 'Book' } | Select-Object -First 1
Check 'whole project: 15 notes from GEN to ACT, book box off' ($c[0] -eq 15 -and (CountsText) -match '^FIX ?:' -and -not $bookBox.Current.IsEnabled) (CountsText)
Shot '4_project.png'
# Row colours (the user's choices). Sorted by reference, last first, the ACT notes are the top rows:
# ACT 2:15 (current row, so in the selection colour), 2:12, 2:10, 2:5, 2:1.
function HeaderClick($name) {
    $hd = All | Where-Object { $_.Current.ControlType -eq $CT::Header -and $_.Current.Name -eq $name } | Select-Object -First 1
    $g = All | Where-Object { $_.Current.ControlType -eq $CT::Table } | Select-Object -First 1
    $a = $hd.Current.BoundingRectangle; $b = $g.Current.BoundingRectangle
    [BnWin]::ClickAt([IntPtr]$g.Current.NativeWindowHandle, ($a.X + $a.Width / 2 - $b.X) / $b.Width, ($a.Y + $a.Height / 2 - $b.Y) / $b.Height)
    Start-Sleep -Milliseconds 700
}
HeaderClick $colRef
if ((Refs)[0] -ne 'ACT 2:15') { HeaderClick $colRef }
# The list keeps the current note and scrolls to it: Page Up makes the first row current and visible.
$gridEl = All | Where-Object { $_.Current.ControlType -eq $CT::Table } | Select-Object -First 1
for ($k = 0; $k -lt 4; $k++) { [BnWin]::PostKey([IntPtr]$gridEl.Current.NativeWindowHandle, 0x21) }
Start-Sleep -Milliseconds 600
$refs = Refs
$colors = @{}
foreach ($ref in @('ACT 2:12', 'ACT 2:10', 'ACT 2:5', 'ACT 2:1')) { $colors[$ref] = CellColor $colRef ([array]::IndexOf($refs, $ref)) }
# Down: ACT 2:12 becomes the current row, and ACT 2:15 shows its own colour.
$gridEl = All | Where-Object { $_.Current.ControlType -eq $CT::Table } | Select-Object -First 1
[BnWin]::PostKey([IntPtr]$gridEl.Current.NativeWindowHandle, 0x28)
Start-Sleep -Milliseconds 500
$colors['ACT 2:15'] = CellColor $colRef ([array]::IndexOf($refs, 'ACT 2:15'))
Shot '4_colours.png'
Check 'colour: unresolved conflict light red' ($colors['ACT 2:5'] -eq 'F8CCCC') $colors['ACT 2:5']
Check 'colour: Wordlist and Biblical Terms notes yellow (resolved too)' ($colors['ACT 2:10'] -eq 'FFF2A8' -and $colors['ACT 2:12'] -eq 'FFF2A8') "$($colors['ACT 2:10']) $($colors['ACT 2:12'])"
Check 'colour: resolved conflict light green' ($colors['ACT 2:15'] -eq 'CCEECC') $colors['ACT 2:15']
Check 'colour: other unresolved notes white' ($colors['ACT 2:1'] -eq 'FFFFFF') $colors['ACT 2:1']
Choose 'Tag' (T '^Conflict$' '^Conflicto$' '^Conflit$')
Check 'tag filter Conflict: both conflict notes in ACT (status All)' ((Counts)[1] -eq 2 -and (Refs) -contains 'ACT 2:5' -and (Refs) -contains 'ACT 2:15') (CountsText)
Choose 'Tag' '^\('
ChooseIndex 'Scope' 1
ChooseIndex 'Status' 0

# ------------------------------------------------------------ 4b. hide filters, the summary line
$sum = SummaryText
Check 'summary: status only when no other filter' ($sum -match (T 'Status: Unresolved$' 'Estado: Sin resolver$' 'Statut : Non r')) $sum
Choose 'CreatedBy' '^Ana Consultora$'
Choose 'Tag' '^Claridad$'
SetText 'Words' 'hijo'
$sum = SummaryText
Check 'summary names each filter in effect' ($sum -match 'Ana Consultora' -and $sum -match 'Claridad' -and $sum -match '"hijo"' -and (Counts)[1] -eq 2) "$sum / $(CountsText)"
$statusBox = All | Where-Object { $_.Current.ControlType -eq $CT::ComboBox -and $_.Current.Name -eq 'Status' } | Select-Object -First 1
Press (T '^Hide filters$' '^Ocultar filtros$' '^Masquer les filtres$')
$statusBox = All | Where-Object { $_.Current.ControlType -eq $CT::ComboBox -and $_.Current.Name -eq 'Status' } | Select-Object -First 1
Check 'hide filters: the filter boxes are gone, the summary and list stay' (-not $statusBox -and (SummaryText) -match 'Claridad' -and (Counts)[1] -eq 2) "$(SummaryText)"
Shot '4b_hidden.png'
CloseWindow | Out-Null
Check 'hide filters remembered' ((Get-Content $settings -Raw) -match '<HideFilters>true</HideFilters>') ''
Launch $null
$statusBox = All | Where-Object { $_.Current.ControlType -eq $CT::ComboBox -and $_.Current.Name -eq 'Status' } | Select-Object -First 1
Check 'reopened: still hidden, filters back to none' (-not $statusBox -and (SummaryText) -notmatch 'Claridad') (SummaryText)
Press (T '^Show filters$' '^Mostrar filtros$' '^Afficher les filtres$')
$statusBox = All | Where-Object { $_.Current.ControlType -eq $CT::ComboBox -and $_.Current.Name -eq 'Status' } | Select-Object -First 1
Check 'show filters: the boxes are back' ($null -ne $statusBox) ''

# ------------------------------------------------------------ 5. resolve two notes
$refs = Refs
Tick 0
Tick 1
Check 'two ticked' ((Counts)[2] -eq 2) (CountsText)
Press ($resolveText + '2\)$')
$question = AnswerYes
Check 'confirmation names the count' ($question -match '2') $question
$w = @(Written | Where-Object { $_.Status -eq 'done' })
Check 'two resolve comments written' ($w.Count -eq 2 -and ($w | ForEach-Object { $_.VerseRef }) -contains ($refs[0] -replace '-\d+$', '')) "$($w.Count)"
Check 'list now 3 unresolved, nothing ticked' ((Counts)[1] -eq 3 -and (Counts)[2] -eq 0) (CountsText)
Check 'results say 2 resolved' ((Results) -match '^2 ') (Results)
Shot '5_resolved.png'

# ------------------------------------------------------------ 6. assign all listed, with a comment
Press $tickAllText
Choose 'AssignTo' '^Rosa Admin$'
SetText 'Comment' 'Por favor revise.'
$assignBtn = All | Where-Object { $_.Current.Name -match '\(3\)$' } | Select-Object -Last 1
[BnWin]::PressButton([IntPtr]$assignBtn.Current.NativeWindowHandle)
$question = AnswerYes
Check 'confirmation shows the comment' ($question -match 'Por favor revise') $question
$w = @(Written | Where-Object { $_.AssignedUser -eq 'Rosa Admin' })
Check 'three assignments written with the comment' ($w.Count -eq 3 -and -not ($w | Where-Object { $_.Contents -ne 'Por favor revise.' })) "$($w.Count)"
$assigned = @(0, 1, 2 | ForEach-Object { CellValue (Cell $colAssigned $_ $CT::Edit) })
Check 'grid shows the new assignment' (-not ($assigned | Where-Object { $_ -ne 'Rosa Admin' })) ($assigned -join ', ')
Check 'comment box emptied' ((GetText 'Comment') -eq '') ''
Shot '6_assigned.png'

# ------------------------------------------------------------ 7. go to
$ref0 = (Refs)[0]
Press $goToText
$outText = CloseWindow
$goto = ($outText -split "`r?`n" | Where-Object { $_ -like 'GOTO*' }) -join ''
Check 'go to the verse' ($goto -match [regex]::Escape("FIX $($ref0 -replace '-\d+$','')")) "$goto / $ref0"

# ------------------------------------------------------------ 8. remembered choices, follow Paratext
Check 'settings saved' ((Test-Path $settings) -and ((Get-Content $settings -Raw) -match '<Scope>1</Scope>')) ''
Launch @{ 'BN_MOVE' = 'MRK 3' }
Start-Sleep -Milliseconds 1500
Check 'follows Paratext to MRK 3' ((ComboText 'Chapter') -eq '3' -and (Counts)[1] -eq 1) (CountsText)
CloseWindow | Out-Null

# ------------------------------------------------------------ 9. an observer can list but not change
Launch @{ 'BN_NOLOCK' = 'FIX' }
$warn = All | Where-Object { $_.Current.Name -match (T 'cannot change the notes' 'no puede cambiar las notas' 'ne pouvez pas modifier') } | Select-Object -First 1
$resolveBtn = FindByText $resolveText
$markBtn = FindByText $markReadText
Check 'observer: warning shown, Resolve off, Mark as read still on' ($warn -and -not $resolveBtn.Current.IsEnabled -and $markBtn.Current.IsEnabled) ''
Shot '9_observer.png'
CloseWindow | Out-Null

# ------------------------------------------------------------ 10. read and unread (1.4)
FreshProjects
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures\ThreadStatus_Test User.ths') -Destination $readStatus
$colRead = T 'Read' "Le$([char]0xED)da" 'Lue'
# The darkest pixels of a cell's text, from a PrintWindow capture: bold text has more of them.
function CellInk($header, $row) {
    $cell = Cell $header $row $CT::Edit
    $cr = $cell.Current.BoundingRectangle
    $h = [IntPtr]$script:win.Current.NativeWindowHandle
    $r = New-Object BnWin+Rect
    [BnWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [BnWin]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $wr = $script:win.Current.BoundingRectangle
    $x0 = [int](($cr.X - $wr.X) / $wr.Width * $bmp.Width); $x1 = [int](($cr.X + $cr.Width - $wr.X) / $wr.Width * $bmp.Width)
    $y0 = [int](($cr.Y - $wr.Y) / $wr.Height * $bmp.Height); $y1 = [int](($cr.Y + $cr.Height - $wr.Y) / $wr.Height * $bmp.Height)
    $n = 0
    for ($x = $x0 + 2; $x -lt $x1 - 2; $x++) { for ($y = $y0 + 2; $y -lt $y1 - 2; $y++) { $px = $bmp.GetPixel($x, $y); if ($px.R + $px.G + $px.B -lt 300) { $n++ } } }
    $g.Dispose(); $bmp.Dispose()
    $n
}
Launch $null
ChooseIndex 'Scope' 1
ChooseIndex 'Status' 0
$refs = Refs
$read = @{}
for ($k = 0; $k -lt $refs.Count; $k++) { $read[$refs[$k]] = CellValue (Cell $colRead $k $CT::Edit) }
$yes = T 'Yes' "S$([char]0xED)" 'Oui'; $no = T 'No' 'No' 'Non'
Check 'Read column: MRK 2:1 and 2:5 read, 2:3 not' ($read['MRK 2:1'] -eq $yes -and $read['MRK 2:5'] -eq $yes -and $read['MRK 2:3'] -eq $no) (($read.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')
# Rows 1 and 2 are MRK 2:3 (unread) and MRK 2:5 (read); row 0 is the current row, drawn selected.
$inkUnread = CellInk $colRef 1; $inkRead = CellInk $colRef 2
Check 'unread row is bold' ($refs[1] -eq 'MRK 2:3' -and $refs[2] -eq 'MRK 2:5' -and $inkUnread -gt $inkRead * 1.15) "$($refs[1]) $inkUnread / $($refs[2]) $inkRead"
ChooseIndex 'Status' 1
ChooseIndex 'Read' 1
Check 'resolved but unread: only MRK 2:20' ((Counts)[1] -eq 1 -and (Refs)[0] -eq 'MRK 2:20') "$(CountsText) $((Refs) -join ', ')"
Check 'summary names the read filter' ((SummaryText) -match (T 'Read: Unread' "Le$([char]0xED)das: No le$([char]0xED)das" 'Lues : Non lues')) (SummaryText)
Shot '10_resolved_unread.png'
ChooseIndex 'Read' 2
Check 'resolved and read: only MRK 2:5' ((Counts)[1] -eq 1 -and (Refs)[0] -eq 'MRK 2:5') "$(CountsText) $((Refs) -join ', ')"
CloseWindow | Out-Null
Check 'read filter remembered' ((Get-Content $settings -Raw) -match '<Read>2</Read>') ''
Launch $null
Check 'reopened: Read box kept' ((ComboText 'Read') -eq (T 'Read' "Le$([char]0xED)das" 'Lues')) (ComboText 'Read')
ChooseIndex 'Read' 0
ChooseIndex 'Status' 0
CloseWindow | Out-Null

# ------------------------------------------------------------ 11. several projects (1.4)
Launch $null "`"$proj2`""
$projectsBtn = FindByText '^FIX\.\.\.$'
Check 'one project at first: the button names it' ($null -ne $projectsBtn) ''
[BnWin]::PressButton([IntPtr]$projectsBtn.Current.NativeWindowHandle)
$dlgTitle = T 'Projects' 'Proyectos' 'Projets'
$byTitle = New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, $dlgTitle)
$dialog = $null
for ($i = 0; $i -lt 30 -and -not $dialog; $i++) {
    Start-Sleep -Milliseconds 200
    $dialog = $root.FindFirst($Scope::Children, (New-Object Windows.Automation.AndCondition (New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)), $byTitle))
    if (-not $dialog) { $dialog = $script:win.FindFirst($Scope::Children, $byTitle) }
}
if (-not $dialog) { throw 'Projects dialog did not appear' }
$listEl = @($dialog.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) | Where-Object { $_.Current.ClassName -match 'LISTBOX' } | Select-Object -First 1
$lh = [IntPtr]$listEl.Current.NativeWindowHandle
Check 'Projects dialog lists both projects' ([BnWin]::ListCount($lh) -eq 2) "$([BnWin]::ListCount($lh))"
[BnWin]::ListClickItem($lh, 1)
Start-Sleep -Milliseconds 500
$ok = @($dialog.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) | Where-Object { $_.Current.Name -match (T '^OK$' '^Aceptar$' '^OK$') } | Select-Object -First 1
[BnWin]::PressButton([IntPtr]$ok.Current.NativeWindowHandle)
Start-Sleep -Milliseconds 1500
$colProject = T 'Project' 'Proyecto' 'Projet'
$projHeader = All | Where-Object { $_.Current.ControlType -eq $CT::Header -and $_.Current.Name -eq $colProject } | Select-Object -First 1
$c = Counts
Check 'two projects: Project column, 7 notes in MRK 2 (5 + 2)' ($projHeader -and $c[0] -eq 7 -and (FindByText '^FIX, TST2\.\.\.$')) (CountsText)
$names = @(0..6 | ForEach-Object { CellValue (Cell $colProject $_ $CT::Edit) })
Check 'the Project column names each note''s project' (($names -contains 'FIX') -and ($names -contains 'TST2')) ($names -join ', ')
Choose 'Tag' '^Preguntas$'
Check 'tag by name across the projects (ids differ): 3 notes' ((Counts)[1] -eq 3) (CountsText)
Shot '11_two_projects.png'
Press $tickAllText
Choose 'AssignTo' '^Beto Segundo$'
$assignBtn = All | Where-Object { $_.Current.Name -match '\(3\)$' } | Select-Object -Last 1
[BnWin]::PressButton([IntPtr]$assignBtn.Current.NativeWindowHandle)
$question = AnswerYes
$res = Results
Check 'assign: TST2 note assigned, FIX notes skipped (Beto is not active in FIX)' ($res -match 'TST2' -and $res -match (T 'not an active user of FIX' 'no es un usuario activo de FIX' "n'est pas un utilisateur actif de FIX")) $res
CloseWindow | Out-Null
Check 'two projects remembered' (([regex]::Matches((Get-Content $settings -Raw), '<Project>')).Count -eq 2) ''
Launch $null "`"$proj2`""
Check 'reopened: both projects listed' ($null -ne (FindByText '^FIX, TST2\.\.\.$') -and (Counts)[0] -eq 7) (CountsText)
CloseWindow | Out-Null

# ------------------------------------------------------------ 12. mark as read (1.5)
FreshProjects
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures\ThreadStatus_Test User.ths') -Destination $readStatus
Launch $null
ChooseIndex 'Status' 2
ChooseIndex 'Read' 1
$c = Counts
Check 'MRK 2, all statuses, unread: 4 (2:3, 2:10-12, 2:14, 2:20)' ($c[1] -eq 4) (CountsText)
$linesBefore = @(Get-Content -LiteralPath $readStatus).Count
Press $tickAllText
Press ($markReadText + '4\)$')
$question = AnswerYes
Check 'confirmation says it is for you only' ($question -match (T 'for you' 'para usted' 'pour vous')) $question
Check 'marked: none unread left, results say 4' ((Counts)[1] -eq 0 -and (Results) -match '^4 ') "$(CountsText) / $(Results)"
Check 'no comment written' (@(Written).Count -eq 0) "$(@(Written).Count)"
Check 'read-status file got the notes' (@(Get-Content -LiteralPath $readStatus).Count -gt $linesBefore) ''
Shot '12_marked_read.png'
ChooseIndex 'Read' 2
Check 'all 7 now read' ((Counts)[1] -eq 7) (CountsText)
# Mark as unread (1.6)
Press $tickAllText
Press ($markUnreadText + '7\)$')
$question = AnswerYes
Check 'mark unread: confirmation, then none read left' ($question -match (T 'as unread' "como no le$([char]0xED)das" 'comme non lues') -and (Counts)[1] -eq 0 -and (Results) -match '^7 ') "$(CountsText) / $(Results)"
Shot '12_marked_unread.png'
ChooseIndex 'Read' 1
Check 'mark unread: all 7 unread' ((Counts)[1] -eq 7) (CountsText)
ChooseIndex 'Read' 0
ChooseIndex 'Status' 0
CloseWindow | Out-Null

"$failures failure(s)"
