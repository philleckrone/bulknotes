<#
.SYNOPSIS
  Makes a narrated demo video of the Bulk Note Editing window (MP4, 1920x1080).
  1. Drives the test host's window (form mode) on a copy of PDT15's Settings.xml plus the test
     fixtures' notes, the same way tests\ui_smoke.ps1 does (posted messages; the mouse is never
     moved), and captures the window (and the confirmation dialogs) after each step.
  2. Draws each frame: a headline, the window with orange boxes round the controls used and a
     pointer, and the narration as subtitles.
  3. Speaks the narration with Windows text-to-speech (System.Speech) and joins the steps with ffmpeg.
  The text is in narration.json (UTF-8). This file stays ASCII.

.EXAMPLE
  .\make_demo.ps1 -Lang es
#>
param(
    [string]$Lang = 'en',
    [string]$Work = '',
    [string]$Project = 'C:\ClaudeCode Projects\TestProject\PDT15',
    # An Azure voice (a name ending in 'Neural', e.g. en-US-JennyNeural; key and region from
    # AZURE_SPEECH_KEY / AZURE_SPEECH_REGION, see azure_tts.ps1) or a Windows voice (System.Speech).
    [string]$Voice = '',
    [int]$Rate = -1,                 # Windows voices: -10..10
    [string]$AzureRate = '0%',       # Azure voices: e.g. '-5%'
    # ffmpeg.exe to use; by default a newer GPL build in C:\ffmpeg-*, else the one on PATH.
    [string]$FFmpeg = '',
    # Zoom into the boxed controls in each step (written as BulkNotes_Demo_XX_zoom.mp4).
    [switch]$Zoom,
    # No narration: each step stays up as long as its subtitle takes to read (for testing).
    [switch]$NoVoice,
    # Background music (looped, kept soft; lowered further while the voice speaks). '' = none.
    [string]$Music = 'C:\Users\Pleckrone\Dropbox\Camtasia work\Background Music Files\Aetheric - Coconut Kind of Love (freetouse.com).mp3',
    [double]$MusicVolume = 0.30,        # the music on its own, at the start (the user's level)
    [double]$MusicUnderVoice = 0.025,    # from the narration on (the user's level)
    [double]$CoverIntro = 3.5,          # seconds of music alone on the cover before the narration
    # The cover's second line, under the plugin's name (narration.json, step "title").
    [string]$Series = 'Plugins by Phil',
    # Dissolve between steps, and dip to black after the cover and before the closing slide (seconds).
    [double]$Dissolve = 1.0,
    # Between two steps that list the same number of notes, a wipe instead (the user's rule:
    # the window hardly changes, so a dissolve would hardly show).
    [double]$Wipe = 2.0,
    [double]$Dip = 1.4
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$tests = Join-Path (Split-Path $here) 'tests'
$exe = Join-Path $tests 'TestHost\bin\Release\BulkNotesTestHost.exe'
if (-not $Work) { $Work = Join-Path $env:TEMP "bulknotes_demo_$Lang" }
if (-not $Voice) { $Voice = @{ 'en' = 'en-US-JennyNeural'; 'es' = 'es-MX-JorgeNeural'; 'fr' = 'fr-FR-RemyMultilingualNeural' }[$Lang] }   # the user's choices
$azure = $Voice -match 'Neural$'
$musicOn = $Music -and (Test-Path -LiteralPath $Music)
if ($Music -and -not $musicOn) { "music file not found, none used: $Music" }
$es = $Lang -like 'es*'; $isFr = $Lang -like 'fr*'
# The window's (and the slides') text in the video's language: English, Spanish, French.
function T($en, $sp, $frText) { if ($es) { $sp } elseif ($isFr -and $null -ne $frText) { $frText } else { $en } }

$narr = Get-Content -LiteralPath (Join-Path $here 'narration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
function Text($id) { ($narr.steps | Where-Object { $_.id -eq $id } | Select-Object -First 1).$Lang }
# Box cues in the narration (the user's request: the boxes follow the voice). "{1}" before a
# phrase shows box 1 (the step's boxes, in order) from there on, "{0,2}" boxes 0 and 2, "{}" none;
# the pointer goes on the first box named. Before the first cue no box is shown. A text without
# cues shows all its boxes the whole time. Cues are left out of the subtitles and the voice.
$cueRx = '\{\s*(\d+(\s*,\s*\d+)*)?\s*\}'
# A word followed by [alias] is said as the alias but shown as written: "read[red]" (the user's
# request: the voice said "reed" where it means "red").
$aliasRx = '(\w+)\[([^\[\]{}]*)\]'
function NoCues($raw) { if ($raw) { [regex]::Replace($raw, $cueRx, '') } else { $raw } }
function Clean($raw) { if ($raw) { [regex]::Replace((NoCues $raw), $aliasRx, '$1') } else { $raw } }
function Spoken($t) { if ($t) { [regex]::Replace($t, $aliasRx, '$2') } else { $t } }
# The parts of a step: @{ Pos (in the clean text); Show (box numbers) }, or none without cues.
function Cues($raw) {
    $clean = ''; $marks = @(); $last = 0
    if (-not $raw) { return , @() }
    foreach ($m in [regex]::Matches($raw, $cueRx)) {
        $clean += $raw.Substring($last, $m.Index - $last)
        $show = @()
        if ($m.Groups[1].Success) { $show = @($m.Groups[1].Value -split '\s*,\s*' | ForEach-Object { [int]$_ }) }
        $marks += [pscustomobject]@{ Pos = $clean.Length; Show = $show }
        $last = $m.Index + $m.Length
    }
    if ($marks.Count -and $marks[0].Pos -gt 0) { $marks = @([pscustomobject]@{ Pos = 0; Show = @() }) + $marks }
    , $marks
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms, System.Speech
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class DmWin {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    /// <summary>Per-monitor aware, so window rectangles, DWM frames and screen copies all use
    /// physical pixels on monitors with different scales.</summary>
    public static void BeAware() { try { if (SetProcessDpiAwarenessContext((IntPtr)(-4))) return; } catch { } SetProcessDPIAware(); }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Rect r, int size);
    /// <summary>The visible frame (without the invisible resize borders), in screen pixels.</summary>
    public static Rect Frame(IntPtr h) {
        Rect r;
        if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Rect))) != 0) GetWindowRect(h, out r);
        return r;
    }
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] static extern IntPtr SendText(IntPtr h, int msg, IntPtr w, string l);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, int flags);
    /// <summary>Keeps a window above all others (no move, no size), so screen copies show it.</summary>
    public static void TopMost(IntPtr h) { SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
    public static void ClickAt(IntPtr h, double fx, double fy) {
        Rect c; GetClientRect(h, out c);
        int x = (int)(fx * c.R), y = (int)(fy * c.B);
        IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
        PostMessage(h, 0x201, (IntPtr)1, lp);
        PostMessage(h, 0x202, IntPtr.Zero, lp);
    }
    public static void PostKey(IntPtr h, int vk) {
        PostMessage(h, 0x100, (IntPtr)vk, (IntPtr)1);
        PostMessage(h, 0x101, (IntPtr)vk, (IntPtr)unchecked((int)0xC0000001));
    }
    public static void PressButton(IntPtr h) { PostMessage(h, 0xF5, IntPtr.Zero, IntPtr.Zero); }
    public static string ComboItem(IntPtr h, int i) {
        int n = (int)SendMessage(h, 0x149, (IntPtr)i, IntPtr.Zero);
        var sb = new StringBuilder(n + 1);
        SendMessage(h, 0x148, (IntPtr)i, sb);
        return sb.ToString();
    }
    public static int ComboCount(IntPtr h) { return (int)SendMessage(h, 0x146, IntPtr.Zero, IntPtr.Zero); }
    public static void ComboSelect(IntPtr h, int i) {
        SendMessage(h, 0x14E, (IntPtr)i, IntPtr.Zero);
        int id = GetDlgCtrlID(h);
        SendMessage(GetParent(h), 0x111, (IntPtr)((1 << 16) | (id & 0xFFFF)), h);
    }
    public static void SetText(IntPtr h, string text) { SendText(h, 0x0C, IntPtr.Zero, text); }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref Rect r);
    /// <summary>A click posted on a list-box item (LB_GETITEMRECT); a CheckedListBox with
    /// CheckOnClick ticks it.</summary>
    public static void ListClickItem(IntPtr h, int i) {
        Rect r = new Rect();
        SendMessage(h, 0x198, (IntPtr)i, ref r);
        IntPtr lp = (IntPtr)((((r.T + r.B) / 2) << 16) | ((r.L + 8) & 0xFFFF));
        PostMessage(h, 0x201, (IntPtr)1, lp);
        PostMessage(h, 0x202, IntPtr.Zero, lp);
    }
}
'@
[DmWin]::BeAware()
$AE = [Windows.Automation.AutomationElement]
$Scope = [Windows.Automation.TreeScope]
$CT = [Windows.Automation.ControlType]
$root = $AE::RootElement

# ------------------------------------------------------------ the demo project
if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
$proj = Join-Path $Work 'PDT15'
$shots = Join-Path $Work 'shots'
$frames = Join-Path $Work 'frames'
New-Item -ItemType Directory -Force $proj, $shots, $frames | Out-Null
Copy-Item -LiteralPath (Join-Path $Project 'Settings.xml') -Destination $proj
Get-ChildItem -LiteralPath (Join-Path $tests 'fixtures') -Filter '*.xml' | Copy-Item -Destination $proj
# A second project (1.4), the fixture TST2, with PDT15's Settings.xml under its own Guid.
$proj2 = Join-Path $Work 'TST2'
Copy-Item -LiteralPath (Join-Path $tests 'fixtures\TST2') -Destination $proj2 -Recurse
$xml = [IO.File]::ReadAllText((Join-Path $Project 'Settings.xml'))
[IO.File]::WriteAllText((Join-Path $proj2 'Settings.xml'), [regex]::Replace($xml, '<Guid>[^<]*</Guid>', '<Guid>tst2000000000000000000000000000000000002</Guid>'))
# Read status (1.4): the fixture's, as Rosa Admin's (BN_USER), in the folder above the projects,
# so some notes are read and the others bold.
Copy-Item -LiteralPath (Join-Path $tests 'fixtures\ThreadStatus_Test User.ths') -Destination (Join-Path $Work 'ThreadStatus_Rosa Admin.ths')
$settings = Join-Path $Work 'settings.xml'

# ------------------------------------------------------------ driving the window
$script:proc = $null
$script:win = $null
trap { if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill() }; throw $_ }
function Launch {
    $psi = New-Object Diagnostics.ProcessStartInfo $exe
    $psi.Arguments = "form `"$proj`" `"$proj2`" --lang $Lang --ref MRK 2 11"
    $psi.EnvironmentVariables['BN_SETTINGS'] = $settings
    $psi.EnvironmentVariables['BN_USER'] = 'Rosa Admin'
    $psi.EnvironmentVariables['BN_LANG'] = $Lang
    foreach ($k in @('BN_MOVE', 'BN_NOLOCK', 'BN_USERS', 'BN_FAIL', 'BN_NORESOLVE')) { $psi.EnvironmentVariables.Remove($k) }
    # Open on a screen without the mouse cursor: hover effects disturb posted clicks.
    $cursor = [Windows.Forms.Cursor]::Position
    $screen = [Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Bounds.Contains($cursor) } | Select-Object -First 1
    if ($screen) { $psi.EnvironmentVariables['BN_LOCATION'] = "$($screen.WorkingArea.X + 10),$($screen.WorkingArea.Y + 10)" }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $script:proc = [Diagnostics.Process]::Start($psi)
    $byProcess = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
    for ($i = 0; $i -lt 100 -and -not $script:win; $i++) {
        Start-Sleep -Milliseconds 200
        $script:win = $root.FindFirst($Scope::Children, $byProcess)
    }
    if (-not $script:win) { throw 'window did not appear' }
    [DmWin]::TopMost([IntPtr]$script:win.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 2000
}
function All { @($script:win.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) }
function ByText($pattern) { All | Where-Object { $_.Current.Name -match $pattern -and $_.Current.Name -notmatch ' Row ' } | Select-Object -First 1 }
function Press($pattern) {
    $el = ByText $pattern
    if (-not $el) { throw "button $pattern not found" }
    [DmWin]::PressButton([IntPtr]$el.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 800
    $el
}
function ComboEl($name) {
    $c1 = New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, $name)
    $c2 = New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $CT::ComboBox)
    $el = $script:win.FindFirst($Scope::Descendants, (New-Object Windows.Automation.AndCondition $c1, $c2))
    if (-not $el) { throw "combo $name not found" }
    $el
}
function Choose($name, $itemPattern) {
    $el = ComboEl $name
    $h = [IntPtr]$el.Current.NativeWindowHandle
    for ($i = 0; $i -lt [DmWin]::ComboCount($h); $i++) {
        if ([DmWin]::ComboItem($h, $i) -match $itemPattern) { [DmWin]::ComboSelect($h, $i); Start-Sleep -Milliseconds 800; return $el }
    }
    throw "$itemPattern not in $name"
}
function ChooseIndex($name, $i) { $el = ComboEl $name; [DmWin]::ComboSelect([IntPtr]$el.Current.NativeWindowHandle, $i); Start-Sleep -Milliseconds 800; $el }
function Cell($header, $row, $type) {
    $c2 = New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $type)
    # The row word follows the window's language ("Marcar Fila 0" in Spanish).
    $want = '^' + [regex]::Escape($header) + ' (Row|Fila|Ligne) ' + $row + '(,|$)'
    @($script:win.FindAll($Scope::Descendants, $c2)) | Where-Object { $_.Current.Name -match $want } | Select-Object -First 1
}
function Header($name) { All | Where-Object { $_.Current.ControlType -eq $CT::Header -and $_.Current.Name -eq $name } | Select-Object -First 1 }
function Grid { All | Where-Object { $_.Current.ControlType -eq $CT::Table } | Select-Object -First 1 }
function HeaderClick($name) {
    $hd = Header $name; $g = Grid
    $a = $hd.Current.BoundingRectangle; $b = $g.Current.BoundingRectangle
    [DmWin]::ClickAt([IntPtr]$g.Current.NativeWindowHandle, ($a.X + $a.Width / 2 - $b.X) / $b.Width, ($a.Y + $a.Height / 2 - $b.Y) / $b.Height)
    Start-Sleep -Milliseconds 800
}
function GridKey($vk, $n) { $g = Grid; for ($k = 0; $k -lt $n; $k++) { [DmWin]::PostKey([IntPtr]$g.Current.NativeWindowHandle, $vk) }; Start-Sleep -Milliseconds 600 }
function FirstRef { $c = Cell $colRef 0 $CT::Edit; if ($c) { $c.GetCurrentPropertyValue([Windows.Automation.ValuePattern]::ValueProperty) } }
function Tick($row) {
    (Cell $colTick $row $CT::CheckBox).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 400
}
function Edits { @(All | Where-Object { $_.Current.ClassName -match '\.EDIT\.' } | Sort-Object { $_.Current.BoundingRectangle.Y }) }
function EditEl($name) { (Edits)[@{ 'Words' = 0; 'Comment' = 1; 'Results' = 2 }[$name]] }
function SetText($name, $text) { $el = EditEl $name; [DmWin]::SetText([IntPtr]$el.Current.NativeWindowHandle, $text); Start-Sleep -Milliseconds 900; $el }
function CountsEl { All | Where-Object { $_.Current.ControlType -eq $CT::Text -and $_.Current.Name -match '^(MRK|PDT15)( \d+(:\d+)?)? ?:' } | Select-Object -First 1 }
function SummaryEl { All | Where-Object { $_.Current.ControlType -eq $CT::Text -and $_.Current.Name -match (T '^Filters:' '^Filtros:' '^Filtres :') } | Select-Object -First 1 }
function LabelEl($text) { All | Where-Object { $_.Current.Name -eq $text } | Select-Object -First 1 }
function FindDialog {
    $byPid = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
    for ($i = 0; $i -lt 40; $i++) {
        $all = @($root.FindAll($Scope::Children, $byPid)) + @($script:win.FindAll($Scope::Children, [Windows.Automation.Condition]::TrueCondition))
        $d = $all | Where-Object { $_.Current.ControlType -eq $CT::Window -and $_.Current.NativeWindowHandle -ne $script:win.Current.NativeWindowHandle } | Select-Object -First 1
        if ($d) { Start-Sleep -Milliseconds 1200; return $d }
        Start-Sleep -Milliseconds 200
    }
    throw 'confirmation dialog did not appear'
}
function AnswerYes($dialog) {
    $yes = @($dialog.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) |
        Where-Object { $_.Current.Name -match "^&?(Yes|S$([char]0xED)|Oui)$" } | Select-Object -First 1
    [DmWin]::PressButton([IntPtr]$yes.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 1800
}

# The Projects dialog (1.4), opened by the Projects button; $null if it does not appear.
function ProjectsDialog {
    $byTitle = New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, (T 'Projects' 'Proyectos' 'Projets'))
    $byPid = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
    for ($i = 0; $i -lt 40; $i++) {
        $d = $root.FindFirst($Scope::Children, (New-Object Windows.Automation.AndCondition $byPid, $byTitle))
        if (-not $d) { $d = $script:win.FindFirst($Scope::Children, $byTitle) }
        if ($d) { Start-Sleep -Milliseconds 800; return $d }
        Start-Sleep -Milliseconds 200
    }
    throw 'Projects dialog did not appear'
}
function InDialog($dialog, $pattern) { @($dialog.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) | Where-Object { $_.Current.Name -match $pattern } | Select-Object -First 1 }
function DialogList($dialog) { @($dialog.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) | Where-Object { $_.Current.ClassName -match 'LISTBOX' } | Select-Object -First 1 }
# A button whose label matches, once its count has followed the ticks (it can lag a moment).
function WaitButton($pattern) {
    for ($i = 0; $i -lt 50; $i++) {
        $b = All | Where-Object { $_.Current.Name -match $pattern } | Select-Object -First 1
        if ($b) { return $b }
        Start-Sleep -Milliseconds 300
    }
    throw "button $pattern not found"
}

$colTick = T 'Pick' 'Marcar' 'Cocher'
$colRef = T 'Reference' 'Referencia' "R$([char]0xE9)f$([char]0xE9)rence"
$colAssigned = T 'Assigned to' 'Asignada a' "Attribu$([char]0xE9)e $([char]0xE0)"

# ------------------------------------------------------------ capturing a step
$script:steps = @()
# The window as it is on the screen (dialog included), in physical pixels. PrintWindow is not
# used: the test host is not DPI aware, so on a scaled monitor it would give the unscaled picture.
function Capture([IntPtr]$h) {
    $f = [DmWin]::Frame($h)
    $bmp = New-Object Drawing.Bitmap ($f.R - $f.L), ($f.B - $f.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($f.L, $f.T, 0, 0, $bmp.Size)
    $g.Dispose()
    @{ Bmp = $bmp; Rect = $f }
}
# A box round some elements, in the capture's pixels (UI Automation gives physical pixels here).
function Box($els, $f) {
    $x1 = 1e9; $y1 = 1e9; $x2 = -1e9; $y2 = -1e9
    foreach ($e in $els) {
        if (-not $e) { continue }
        $b = $e.Current.BoundingRectangle
        if ($b.IsEmpty) { continue }
        $x1 = [Math]::Min($x1, $b.X); $y1 = [Math]::Min($y1, $b.Y)
        $x2 = [Math]::Max($x2, $b.X + $b.Width); $y2 = [Math]::Max($y2, $b.Y + $b.Height)
    }
    if ($x2 -lt $x1) { return $null }
    , @([int]($x1 - $f.L), [int]($y1 - $f.T), [int]($x2 - $x1), [int]($y2 - $y1))
}
# $groups: a list of element lists, one orange box each. $pointer: index of the box to point at, or -1.
# $pointEl: an element to put the pointer on instead (e.g. the chosen item of an open list).
function Step($id, $groups, $pointer = -1, $dialog = $null, $pointEl = $null) {
    Start-Sleep -Milliseconds 400
    $main = Capture ([IntPtr]$script:win.Current.NativeWindowHandle)
    $boxes = @()
    foreach ($grp in $groups) { $b = Box @($grp) $main.Rect; if ($b) { $boxes += , $b } }
    $pointAt = $null
    if ($pointEl) { $pointAt = Box @($pointEl) $main.Rect }
    $dlgBox = $null
    if ($dialog) {
        $d = [DmWin]::Frame([IntPtr]$dialog.Current.NativeWindowHandle)
        $dlgBox = @(($d.L - $main.Rect.L), ($d.T - $main.Rect.T), ($d.R - $d.L), ($d.B - $d.T))
    }
    $file = Join-Path $shots "$id.png"
    $main.Bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    $main.Bmp.Dispose()
    # The number of notes listed ("MRK 2: 5 note(s) read, 5 listed, 0 ticked."), -1 if not found.
    $rows = -1
    $ce = CountsEl
    if ($ce -and $ce.Current.Name -match ': (\d+)\D+(\d+)') { $rows = [int]$Matches[2] }
    $script:steps += [pscustomobject]@{ Id = $id; Shot = $file; Boxes = $boxes; Pointer = [int]$pointer; Dialog = $dlgBox; Rows = $rows; PointAt = $pointAt }
    "captured $id"
}

# Opens a combo box's list (CB_SHOWDROPDOWN; the mouse is not used), so a step shows the choices
# (the user's request). Returns the open list and its selected item (UI Automation), or $null.
function OpenList($comboEl) {
    $h = [IntPtr]$comboEl.Current.NativeWindowHandle
    [DmWin]::SendMessage($h, 0x14F, [IntPtr]1, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 800
    $lc = New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $CT::List)
    $list = $comboEl.FindFirst($Scope::Descendants, $lc)
    if (-not $list) {
        # The dropped list is its own top-level window (class ComboLBox).
        $byPid = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
        $list = @($root.FindAll($Scope::Children, $byPid)) | Where-Object { $_.Current.ClassName -eq 'ComboLBox' } | Select-Object -First 1
    }
    $sel = $null
    if ($list) {
        $ic = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.SelectionItemPattern]::IsSelectedProperty, $true)
        $sel = $list.FindFirst($Scope::Descendants, $ic)
    }
    "open list: $(if ($list) { 'found' } else { 'NOT FOUND' }), chosen item: $(if ($sel) { $sel.Current.Name } else { 'not found' })" | Out-Host
    @{ List = $list; Selected = $sel }
}
function CloseList($comboEl) {
    [DmWin]::SendMessage([IntPtr]$comboEl.Current.NativeWindowHandle, 0x14F, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 500
}

# ------------------------------------------------------------ the demo
Launch
Step 's01_open' @(, @((ComboEl 'Scope'), (ComboEl 'Chapter'))) -1
Step 's01_open_b' @(, @(CountsEl)) -1   # (merged into s01 below)

# Several projects (1.4): the Projects dialog with TST2 ticked, then both projects in the list.
$projBtn = Press '^PDT15\.\.\.$'
$pdlg = ProjectsDialog
$plist = DialogList $pdlg
[DmWin]::ListClickItem([IntPtr]$plist.Current.NativeWindowHandle, 1)
Start-Sleep -Milliseconds 700
$okBtn = InDialog $pdlg (T '^OK$' '^Aceptar$' '^OK$')
Step 's01b_projects' @(@($projBtn), @($plist), @($okBtn)) 0 $pdlg
[DmWin]::PressButton([IntPtr]$okBtn.Current.NativeWindowHandle)
Start-Sleep -Milliseconds 2000
$projBtn = ByText '^PDT15, TST2\.\.\.$'
if (-not $projBtn) { throw 'TST2 was not added' }
$colProject = T 'Project' 'Proyecto' 'Projet'
$projCol = @(Header $colProject) + @(0..6 | ForEach-Object { Cell $colProject $_ $CT::Edit })
Step 's01c_two' @(@($projBtn), $projCol, @(CountsEl)) -1
# Back to PDT15 alone for the rest of the demo.
Press '^PDT15, TST2\.\.\.$' | Out-Null
$pdlg = ProjectsDialog
[DmWin]::ListClickItem([IntPtr](DialogList $pdlg).Current.NativeWindowHandle, 1)
Start-Sleep -Milliseconds 700
[DmWin]::PressButton([IntPtr](InDialog $pdlg (T '^OK$' '^Aceptar$' '^OK$')).Current.NativeWindowHandle)
Start-Sleep -Milliseconds 2000
if (-not (ByText '^PDT15\.\.\.$')) { throw 'TST2 was not removed' }

$scopeEl = ChooseIndex 'Scope' 2
$ol = OpenList $scopeEl
Step 's02_scope' @(@($scopeEl, $ol.List), @(CountsEl)) -1 $null $ol.Selected
CloseList $scopeEl

ChooseIndex 'Scope' 3 | Out-Null
ChooseIndex 'Status' 2 | Out-Null
HeaderClick $colRef
if ((FirstRef) -ne 'ACT 2:15') { HeaderClick $colRef }
GridKey 0x21 4          # Page Up: the first row current
GridKey 0x28 4          # Down x4: ACT 2:1 (a white row) current, so the coloured rows show their colours
Step 's03_colours' @(@(LabelEl (T 'Resolved' 'Resuelta' "R$([char]0xE9)solue")), @(LabelEl (T 'Wordlist or Biblical Terms' "Lista de palabras o T$([char]0xE9)rminos b$([char]0xED)blicos" 'Liste de mots ou Termes bibliques')), @(LabelEl (T 'Unresolved conflict' 'Conflicto sin resolver' "Conflit non r$([char]0xE9)solu")), @(LabelEl (T 'Bold: unread' "Negrita: no le$([char]0xED)da" 'Gras : non lue'))) -1

# Read and unread (1.4-1.6): resolved notes not read yet, then marked as read.
ChooseIndex 'Status' 1 | Out-Null
$readEl = ChooseIndex 'Read' 1
$ol = OpenList $readEl
Step 's03b_read' @(@($readEl, $ol.List), @(ComboEl 'Status'), @(Grid)) -1 $null $ol.Selected
CloseList $readEl
$tickAll0 = Press (T '^Tick all listed$' '^Marcar todas las mostradas$' "^Cocher toutes les notes affich$([char]0xE9)es$")
$markRead = WaitButton (T '^Mark ticked as read \([1-9]' "^Marcar las marcadas como le$([char]0xED)das \([1-9]" "^Marquer les coch$([char]0xE9)es comme lues \([1-9]")
$markUnread = ByText (T '^Mark ticked as unread \(' "^Marcar las marcadas como no le$([char]0xED)das \(" "^Marquer les coch$([char]0xE9)es comme non lues \(")
Step 's03c_markread' @(@($tickAll0), @($markRead), @($markUnread)) 1
[DmWin]::PressButton([IntPtr]$markRead.Current.NativeWindowHandle)
Start-Sleep -Milliseconds 800
AnswerYes (FindDialog)
Step 's03d_marked' @(@(EditEl 'Results'), @(CountsEl)) -1
ChooseIndex 'Read' 0 | Out-Null
ChooseIndex 'Status' 2 | Out-Null

$tagEl = Choose 'Tag' (T '^Conflict$' '^Conflicto$' '^Conflit$')
$ol = OpenList $tagEl
Step 's04_tag' @(@($tagEl, $ol.List), @(Grid)) -1 $null $ol.Selected
CloseList $tagEl

Press (T '^Clear filters$' '^Quitar filtros$' '^Enlever les filtres$') | Out-Null
# Two steps (the user's request): Created by with its list open, then the word, which the open
# list would cover.
$cb = Choose 'CreatedBy' '^Luis Revisor$'
$ol = OpenList $cb
Step 's05_filters' @(, @($cb, $ol.List)) -1 $null $ol.Selected
CloseList $cb
$words = SetText 'Words' 'revisar'
# Box 2: the Hide filters button, still reading "Hide filters" (the narration names it here).
$hideEl = ByText (T '^Hide filters$' '^Ocultar filtros$' '^Masquer les filtres$')
Step 's05b_words' @(@($words), @(SummaryEl), @($hideEl)) -1

$hideBtn = Press (T '^Hide filters$' '^Ocultar filtros$' '^Masquer les filtres$')
$hideBtn = ByText (T '^Show filters$' '^Mostrar filtros$' '^Afficher les filtres$')
Step 's06_hide' @(@($hideBtn), @(SummaryEl)) 0
Press (T '^Show filters$' '^Mostrar filtros$' '^Afficher les filtres$') | Out-Null
Press (T '^Clear filters$' '^Quitar filtros$' '^Enlever les filtres$') | Out-Null

ChooseIndex 'Scope' 1 | Out-Null
ChooseIndex 'Status' 0 | Out-Null
HeaderClick $colRef
if ((FirstRef) -notmatch '^MRK 2:1$') { HeaderClick $colRef }
GridKey 0x21 2
Tick 0
Tick 1
# The "Comments of this note:" label and the box under it (the narration speaks about it).
$commentsLabel = LabelEl (T 'Comments of this note:' 'Comentarios de esta nota:' 'Commentaires de cette note :')
$commentsBox = All | Where-Object { $_.Current.ClassName -match '\.LISTBOX\.' } | Sort-Object { $_.Current.BoundingRectangle.Y } | Select-Object -First 1
"comments box: label $([bool]$commentsLabel), box $(if ($commentsBox) { $commentsBox.Current.ClassName } else { 'not found' })"
Step 's07_tick' @(@((Cell $colTick 0 $CT::CheckBox), (Cell $colTick 1 $CT::CheckBox)), @($commentsLabel, $commentsBox)) 0

$commentEl = SetText 'Comment' $narr.comment
$resolveBtn = Press (T '^Resolve ticked \(2\)$' '^Resolver marcadas \(2\)$' "^R$([char]0xE9)soudre les coch$([char]0xE9)es \(2\)$")
$dlg = FindDialog
Step 's08_resolve' @(@($commentEl), @($resolveBtn)) 1 $dlg
AnswerYes $dlg
Step 's09_resolved' @(@(EditEl 'Results'), @(CountsEl)) -1

$tickAll = Press (T '^Tick all listed$' '^Marcar todas las mostradas$' "^Cocher toutes les notes affich$([char]0xE9)es$")
$assignEl = Choose 'AssignTo' '^Rosa Admin$'
# Wait for the count on the button to follow the ticks (it can lag a moment).
$assignBtn = $null
for ($w8 = 0; $w8 -lt 50 -and -not $assignBtn; $w8++) { $assignBtn = All | Where-Object { $_.Current.Name -match (T '^Assign.*\(3\)$' '^Asignar.*\(3\)$' '^Attribuer.*\(3\)$') } | Select-Object -Last 1; if (-not $assignBtn) { Start-Sleep -Milliseconds 300 } }
if (-not $assignBtn) { throw 'Assign ticked (3) not found' }
# The step shows the Assign to list open (the narration speaks about who is on it).
$ol = OpenList $assignEl
Step 's10_assign' @(@($tickAll), @($assignEl, $ol.List), @($assignBtn)) -1 $null $ol.Selected
CloseList $assignEl
[DmWin]::PressButton([IntPtr]$assignBtn.Current.NativeWindowHandle)
Start-Sleep -Milliseconds 800
$dlg = FindDialog
AnswerYes $dlg
Step 's11_assigned' @(@((Header $colAssigned), (Cell $colAssigned 2 $CT::Edit)), @(EditEl 'Results')) -1

$goto = ByText (T '^Go to in Paratext$' "^Ir al vers$([char]0xED)culo en Paratext$" '^Aller au verset dans Paratext$')
Step 's12_goto' @(, @($goto)) 0

$script:win.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
$script:proc.WaitForExit(10000) | Out-Null

# s01: one frame with both boxes
$a = $script:steps | Where-Object { $_.Id -eq 's01_open' }
$b = $script:steps | Where-Object { $_.Id -eq 's01_open_b' }
$a.Boxes = @($a.Boxes) + @($b.Boxes)
$script:steps = @($script:steps | Where-Object { $_.Id -ne 's01_open_b' })

# The Paratext Tools menu (the user's screenshot, tools_menu_<lang>.png), before the window opens.
# The boxes are in each picture's pixels: the Tools heading and the plugin's menu item
# (tools_menu_en.png 1286 x 923, tools_menu_es.png 1466 x 926, tools_menu_fr.png 1629 x 927); a new screenshot needs new numbers.
$menuBoxes = @{
    'en' = @(@(1036, 102, 54, 24), @(1038, 304, 144, 24))
    'es' = @(@(1126, 102, 112, 24), @(1128, 304, 186, 24))
    'fr' = @(@(1284, 102, 52, 24), @(1286, 304, 204, 24))
}
$menuShot = Join-Path $here "tools_menu_$Lang.png"
if (Test-Path -LiteralPath $menuShot) {
    $mb = @(); foreach ($b in $menuBoxes[$Lang]) { $mb += , $b }
    $menuStep = [pscustomobject]@{ Id = 's00_menu'; Shot = $menuShot; Boxes = $mb; Pointer = 1; Dialog = $null; Rows = -1 }
    $script:steps = @($menuStep) + $script:steps
}
else { "no $menuShot : the menu step is left out" }

# Getting and installing the tool: the user's screenshots, after the last step. Each is
# demo\<File>_<lang>.png, or the English one when there is none for the language (a step with
# neither is left out, e.g. i3a_downloads in English). Boxes are in that picture's pixels
# (x, y, width, height), per language when the pictures differ; Pointer = the box the pointer is on.
# Getting and installing the tool: the shared install section (Install_Phils_tools.exe, see
# tools\demo_video\install\InstallSection.ps1), then this plugin's Tools menu again.
. (Join-Path (Split-Path (Split-Path $here)) 'tools\demo_video\install\InstallSection.ps1')
$script:steps += Get-InstallSteps -Plugin 'BulkNotes' -Lang $Lang -Name (Text 'title').title
$installSteps = @(
    @{ Id = 'i5_ready';    File = 'tools_menu';         Boxes = 'menu'; Pointer = 1 }
)
foreach ($is in $installSteps) {
    $shot = Join-Path $here ("{0}_{1}.png" -f $is.File, $Lang)
    if (-not (Test-Path -LiteralPath $shot)) { $shot = Join-Path $here ("{0}_en.png" -f $is.File) }
    if (-not (Test-Path -LiteralPath $shot)) { "no $shot : step $($is.Id) is left out"; continue }
    $shotLang = if ($shot -like "*_$Lang.png") { $Lang } else { 'en' }
    $boxList = $is.Boxes
    if ($boxList -is [hashtable]) { $boxList = $boxList[$shotLang] }
    elseif ($boxList -eq 'menu') { $boxList = $menuBoxes[$shotLang] }
    $bx = @(); foreach ($b in $boxList) { $bx += , $b }
    $script:steps += [pscustomobject]@{ Id = $is.Id; Shot = $shot; Boxes = $bx; Pointer = $is.Pointer; Dialog = $null; Rows = -1 }
}

# ------------------------------------------------------------ frames
$W = 1920; $H = 1080; $top = 96; $bottomBand = 190
# The "Sky" design (the user's choice): deep blue cover, light-to-mid blue steps, gold accents.
function Hex($h) { [Drawing.ColorTranslator]::FromHtml($h) }
$navy = Hex '#1F3A5F'          # headline bar
$orange = [Drawing.Color]::FromArgb(255, 140, 0)   # boxes round the controls
$gold = Hex '#F2B544'          # accents
$band = Hex '#18222E'          # subtitle band
$bgTop = Hex '#EEF7FF'; $bgBottom = Hex '#94BEE4'          # steps: the gradient
$coverTop = Hex '#2C5A9A'; $coverBottom = Hex '#0B1830'    # cover and closing slide
function Gradient($g, $x, $y, $w, $h, $c1, $c2) {
    $br = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.RectangleF $x, ($y - 1), $w, ($h + 2)), $c1, $c2, 90.0
    $g.FillRectangle($br, [float]$x, [float]$y, [float]$w, [float]$h)
    $br.Dispose()
}
function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc([float]$x, [float]$y, $d, $d, 180, 90); $p.AddArc([float]($x + $w - $d), [float]$y, $d, $d, 270, 90)
    $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, 0, 90); $p.AddArc([float]$x, [float]($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure(); $p
}
function NewFrame { $f = New-Object Drawing.Bitmap $W, $H; $g = [Drawing.Graphics]::FromImage($f); $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'; @{ F = $f; G = $g } }
function Centered($g, $text, $font, $brush, $rect) {
    $sf = New-Object Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString($text, $font, $brush, $rect, $sf)
}
# A font of the given size, or smaller until the text fits on one line in $maxWidth pixels
# (long names, e.g. "Edicion de notas en lote", were cut off on the cover).
function FitFont($g, $text, $family, $size, $maxWidth) {
    $f = New-Object Drawing.Font $family, $size
    while ($size -gt 36 -and $g.MeasureString($text, $f).Width -gt $maxWidth) {
        $f.Dispose(); $size -= 2; $f = New-Object Drawing.Font $family, $size
    }
    $f
}
function Card($id, $file) {
    $t = Text $id
    $fr = NewFrame; $g = $fr.G
    Gradient $g 0 0 $W $H $coverTop $coverBottom
    $light = New-Object Drawing.SolidBrush $gold
    $grey = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(190, 205, 225))
    if ($id -eq 'title') {
        # The cover: a framed panel (gold border) with a tag on its top edge; the plugin's name
        # large and white, a gold rule, then the series smaller (the user's order).
        $px = 360; $py = 290; $pw = $W - 720; $ph = 470
        $panel = RoundRect $px $py $pw $ph 28
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(40, 255, 255, 255))), $panel)
        $g.DrawPath((New-Object Drawing.Pen $gold, 4), $panel)
        $tagFont = New-Object Drawing.Font 'Segoe UI Semibold', 20
        $tagText = T 'PARATEXT PLUGIN' 'COMPLEMENTO DE PARATEXT' 'MODULE PARATEXT'
        $tw = $g.MeasureString($tagText, $tagFont).Width + 44; $th = 48; $tx = ($W - $tw) / 2; $ty = $py - $th / 2
        $g.FillPath((New-Object Drawing.SolidBrush $gold), (RoundRect $tx $ty $tw $th 24))
        Centered $g $tagText $tagFont (New-Object Drawing.SolidBrush $coverBottom) (New-Object Drawing.RectangleF $tx, $ty, $tw, $th)
        Centered $g $t.title (FitFont $g $t.title 'Segoe UI Semibold' 80 ($pw - 60)) ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF $px, ($py + 60), $pw, 170)
        $g.FillRectangle($light, [float]($W / 2 - 160), [float]($py + 250), 320, 5)
        Centered $g $Series (New-Object Drawing.Font 'Segoe UI', 50) $light (New-Object Drawing.RectangleF $px, ($py + 280), $pw, 120)
    }
    elseif ($id -like 'sec_*') {
        # A section slide (the user's "subtitle" slide): the cover's panel, its title large and
        # white, a gold rule, then the plugin's name smaller.
        $px = 360; $py = 290; $pw = $W - 720; $ph = 470
        $panel = RoundRect $px $py $pw $ph 28
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(40, 255, 255, 255))), $panel)
        $g.DrawPath((New-Object Drawing.Pen $gold, 4), $panel)
        Centered $g $t.title (FitFont $g $t.title 'Segoe UI Semibold' 80 ($pw - 60)) ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF $px, ($py + 60), $pw, 170)
        $g.FillRectangle($light, [float]($W / 2 - 160), [float]($py + 250), 320, 5)
        Centered $g (Text 'title').title (New-Object Drawing.Font 'Segoe UI', 50) $light (New-Object Drawing.RectangleF $px, ($py + 280), $pw, 120)
    }
    else {
        $f1 = New-Object Drawing.Font 'Segoe UI Semibold', 72
        $f2 = New-Object Drawing.Font 'Segoe UI', 34
        Centered $g $t.title $f1 ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF 80, 300, ($W - 160), 160)
        $g.FillRectangle($light, [float]($W / 2 - 160), 462, 320, 5)
        Centered $g $t.sub $f2 $light (New-Object Drawing.RectangleF 80, 480, ($W - 160), 120)
        # Credits, in small print.
        $credits = @(T 'Created with Claude AI' 'Creado con Claude AI' "Cr$([char]0xE9)$([char]0xE9) avec Claude AI")
        if (-not $NoVoice -and $azure) { $credits += T 'Narration: AI-generated voice (Microsoft Azure AI Speech)' "Narraci$([char]0xF3)n: voz generada por IA (Microsoft Azure AI Speech)" "Narration : voix g$([char]0xE9)n$([char]0xE9)r$([char]0xE9)e par IA (Microsoft Azure AI Speech)" }
        if ($musicOn) { $credits += T 'Music: Coconut Kind of Love by Aetheric (freetouse.com)' "M$([char]0xFA)sica: Coconut Kind of Love de Aetheric (freetouse.com)" 'Musique : Coconut Kind of Love par Aetheric (freetouse.com)' }
        $f3 = New-Object Drawing.Font 'Segoe UI', 22
        $y = $H - 70 - 42 * $credits.Count
        foreach ($c in $credits) { Centered $g $c $f3 $grey (New-Object Drawing.RectangleF 80, $y, ($W - 160), 42); $y += 42 }
    }
    $g.Dispose(); $fr.F.Save($file, [Drawing.Imaging.ImageFormat]::Png); $fr.F.Dispose()
}
function Pointer($g, $x, $y) {
    # A mouse arrow with its tip at (x, y).
    $s = 1.6
    $pts = @(@(0, 0), @(0, 25), @(6, 19), @(11, 30), @(15, 28), @(10, 18), @(18, 18)) | ForEach-Object { New-Object Drawing.PointF ($x + $_[0] * $s), ($y + $_[1] * $s) }
    $g.FillPolygon([Drawing.Brushes]::White, [Drawing.PointF[]]$pts)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::Black), 2
    $g.DrawPolygon($pen, [Drawing.PointF[]]$pts)
}
# The window area of a frame: the band between the headline and the subtitles, less margins.
$areaX = 40; $areaY = $top + 22; $areaW = $W - 80; $areaH = $H - $top - $bottomBand - 44
# The view of the whole window: the shot widened (or heightened) to the area's shape.
function FullView($iw, $ih) {
    $ar = $areaW / $areaH
    if ($iw / $ih -lt $ar) { $w0 = $ih * $ar; , @((($iw - $w0) / 2), 0, $w0, $ih) }
    else { $h0 = $iw / $ar; , @(0, (($ih - $h0) / 2), $iw, $h0) }
}
# The close-up of a step: the boxes (and the dialog) with a margin, in the area's shape, at most
# twice the size of the full view; $null when that would hardly be closer than the full view.
function ZoomView($st, $iw, $ih) {
    $rects = @($st.Boxes)
    if ($st.Dialog) { $rects += , $st.Dialog }
    if ($rects.Count -eq 0) { return $null }
    $x1 = 1e9; $y1 = 1e9; $x2 = -1e9; $y2 = -1e9
    foreach ($r in $rects) {
        $x1 = [Math]::Min($x1, $r[0]); $y1 = [Math]::Min($y1, $r[1])
        $x2 = [Math]::Max($x2, $r[0] + $r[2]); $y2 = [Math]::Max($y2, $r[1] + $r[3])
    }
    $pad = 0.05 * $iw
    $x1 -= $pad; $y1 -= $pad; $x2 += $pad; $y2 += $pad
    $ar = $areaW / $areaH
    $w = $x2 - $x1; $h = $y2 - $y1
    if ($w / $h -lt $ar) { $w = $h * $ar } else { $h = $w / $ar }
    $full = FullView $iw $ih
    $maxZoom = 2.0
    if ($full[2] / $w -gt $maxZoom) { $w = $full[2] / $maxZoom; $h = $w / $ar }
    if ($full[2] / $w -lt 1.15) { return $null }
    $cx = ($x1 + $x2) / 2; $cy = ($y1 + $y2) / 2
    $x = $cx - $w / 2; $y = $cy - $h / 2
    if ($w -le $iw) { $x = [Math]::Max(0, [Math]::Min($x, $iw - $w)) } else { $x = ($iw - $w) / 2 }
    if ($h -le $ih) { $y = [Math]::Max(0, [Math]::Min($y, $ih - $h)) } else { $y = ($ih - $h) / 2 }
    , @($x, $y, $w, $h)
}
# Between two views, at s (0..1): the centre moves in a straight line, the size changes
# geometrically, so the zoom feels even.
function MixView($a, $b, $s) {
    $w = $a[2] * [Math]::Pow($b[2] / $a[2], $s); $h = $a[3] * [Math]::Pow($b[3] / $a[3], $s)
    $cx = ($a[0] + $a[2] / 2) * (1 - $s) + ($b[0] + $b[2] / 2) * $s
    $cy = ($a[1] + $a[3] / 2) * (1 - $s) + ($b[1] + $b[3] / 2) * $s
    , @(($cx - $w / 2), ($cy - $h / 2), $w, $h)
}
# $part: one part of a step with cues (its Show = the boxes shown); $null = all boxes.
function StepFrame($st, $num, $total, $file, $view = $null, $part = $null) {
    $t = Text $st.Id
    $fr = NewFrame; $g = $fr.G
    Gradient $g 0 $top $W ($H - $top - $bottomBand) $bgTop $bgBottom
    # headline, with a gold line under it
    $g.FillRectangle((New-Object Drawing.SolidBrush $navy), 0, 0, $W, $top)
    $g.FillRectangle((New-Object Drawing.SolidBrush $gold), 0, ($top - 5), $W, 5)
    $fh = New-Object Drawing.Font 'Segoe UI Semibold', 30
    $g.DrawString($t.title, $fh, [Drawing.Brushes]::White, 40, 22)
    $fn = New-Object Drawing.Font 'Segoe UI', 20
    $sf = New-Object Drawing.StringFormat; $sf.Alignment = 'Far'
    $g.DrawString("$num / $total", $fn, (New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(190, 205, 225))), (New-Object Drawing.RectangleF 0, 34, ($W - 40), 40), $sf)
    # the window: the view (a rectangle of the shot) fills the area; float coordinates, so a
    # zoom moves smoothly. Whatever falls outside the band between headline and subtitles is cut.
    $img = [Drawing.Image]::FromFile($st.Shot)
    if (-not $view) { $view = FullView $img.Width $img.Height }
    $sc = [Math]::Min($areaW / $view[2], $areaH / $view[3])
    $ox = $areaX + ($areaW - $view[2] * $sc) / 2 - $view[0] * $sc
    $oy = $areaY + ($areaH - $view[3] * $sc) / 2 - $view[1] * $sc
    $iw = $img.Width * $sc; $ih = $img.Height * $sc
    $g.SetClip((New-Object Drawing.RectangleF 0, $top, $W, ($H - $top - $bottomBand)))
    # a soft shadow (spread, faint layers) and a white frame, so the grey window floats on the page
    for ($k = 12; $k -ge 2; $k -= 2) {
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(12, 10, 30, 60))), (RoundRect ($ox - $k + 6) ($oy - $k + 10) ($iw + 2 * $k) ($ih + 2 * $k) (10 + $k)))
    }
    $g.FillPath([Drawing.Brushes]::White, (RoundRect ($ox - 6) ($oy - 6) ($iw + 12) ($ih + 12) 10))
    $g.DrawImage($img, [float]$ox, [float]$oy, [float]$iw, [float]$ih)
    $pen = New-Object Drawing.Pen $orange, 5
    if ($st.Dialog) {
        $d = $st.Dialog
        $g.DrawRectangle((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(31, 58, 95)), 4), [float]($ox + $d[0] * $sc - 4), [float]($oy + $d[1] * $sc - 4), [float]($d[2] * $sc + 8), [float]($d[3] * $sc + 8))
    }
    $i = 0; $pointAtShown = -not $part
    foreach ($b in $st.Boxes) {
        $shown = (-not $part) -or ($part.Show -contains $i)
        if ($shown) {
            $bx = $ox + $b[0] * $sc - 6; $by = $oy + $b[1] * $sc - 6; $bwid = $b[2] * $sc + 12; $bhei = $b[3] * $sc + 12
            $g.DrawRectangle($pen, [float]$bx, [float]$by, [float]$bwid, [float]$bhei)
            # With cues, the pointer (if the step has one) is on the first box named.
            $pointHere = if ($part) { $st.Pointer -ge 0 -and $part.Show[0] -eq $i } else { $i -eq $st.Pointer }
            if ($pointHere) { Pointer $g ($bx + $bwid * 0.88) ($by + $bhei * 0.6) }   # right end: the text stays readable
            if ($st.PointAt) {
                $pa = $st.PointAt; $cx = $pa[0] + $pa[2] / 2; $cy = $pa[1] + $pa[3] / 2
                if ($cx -ge $b[0] -and $cx -le $b[0] + $b[2] -and $cy -ge $b[1] -and $cy -le $b[1] + $b[3]) { $pointAtShown = $true }
            }
        }
        $i++
    }
    if ($st.PointAt -and $pointAtShown) {
        $pa = $st.PointAt
        # at the right end of the item, so its text stays readable
        Pointer $g ($ox + ($pa[0] + $pa[2] * 0.88) * $sc) ($oy + ($pa[1] + $pa[3] * 0.5) * $sc)
    }
    $g.ResetClip()
    $iwShot = $img.Width; $ihShot = $img.Height
    $img.Dispose()
    # subtitles
    $g.FillRectangle((New-Object Drawing.SolidBrush $band), 0, ($H - $bottomBand), $W, $bottomBand)
    $fs = New-Object Drawing.Font 'Segoe UI', 27
    Centered $g (Clean $t.text) $fs ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF 70, ($H - $bottomBand + 8), ($W - 140), ($bottomBand - 16))
    $g.Dispose(); $fr.F.Save($file, [Drawing.Imaging.ImageFormat]::Png); $fr.F.Dispose()
}

$order = @('title')
foreach ($st in $script:steps) {
    if ($st.Id -eq 'd1_folder') { $order += 'sec_install' }
    $order += $st.Id
}
$order += 'end'
# Slides (cover, section slides, closing slide) have no step number.
function IsCard($id) { $id -eq 'title' -or $id -eq 'end' -or $id -like 'sec_*' }
$stepIds = @($order | Where-Object { -not (IsCard $_) })
# Zoom timing: it starts when the dissolve into the step is over, and takes $zoomTime seconds.
$zoomStart = 1.3; $zoomTime = 1.6; $zoomFrames = [int]($zoomTime * 30)
$zooms = @{}; $partFrames = @{}
$n = 0
foreach ($id in $order) {
    $file = Join-Path $frames ('{0:D2}_{1}.png' -f $n, $id)
    if (IsCard $id) { Card $id $file }
    else {
        $st = $script:steps | Where-Object { $_.Id -eq $id }
        $num = [array]::IndexOf($stepIds, $id) + 1
        StepFrame $st $num $stepIds.Count $file
        $cues = Cues (Text $id).text
        if ($cues.Count) {
            $pf = @()
            for ($k = 0; $k -lt $cues.Count; $k++) {
                $cfile = Join-Path $frames ('{0:D2}_{1}_c{2}.png' -f $n, $id, $k)
                StepFrame $st $num $stepIds.Count $cfile $null $cues[$k]
                $pf += $cfile
            }
            $partFrames[$n] = $pf
            "cues $id : $(($cues | ForEach-Object { '{' + ($_.Show -join ',') + '}' }) -join ' ')"
        }
        elseif ($Zoom) {
            $img = [Drawing.Image]::FromFile($st.Shot); $iw0 = $img.Width; $ih0 = $img.Height; $img.Dispose()
            $v1 = ZoomView $st $iw0 $ih0
            if ($v1) {
                $v0 = FullView $iw0 $ih0
                $zf = @()
                for ($k = 1; $k -le $zoomFrames; $k++) {
                    $pp = $k / $zoomFrames
                    $ss = $pp * $pp * (3 - 2 * $pp)     # ease in and out
                    $zfile = Join-Path $frames ('{0:D2}_z{1:D3}.png' -f $n, $k)
                    StepFrame $st $num $stepIds.Count $zfile (MixView $v0 $v1 $ss)
                    $zf += $zfile
                }
                $zooms[$n] = $zf
                "zoom $id"
            }
        }
    }
    $n++
}

# ------------------------------------------------------------ speech and video
# Spoken text is kept between runs ($Work is emptied each time), so a rebuild asks Azure only for
# what changed (the free tier allows few requests a minute).
$ttsCache = Join-Path $env:TEMP 'bulknotes_demo_tts'
New-Item -ItemType Directory -Force $ttsCache | Out-Null
function Say($text, $wav) {
    $text = Spoken $text
    if (-not $azure) { $tts.SetOutputToWaveFile($wav); $tts.Speak($text); $tts.SetOutputToNull(); return }
    $keyText = "$Voice|$AzureRate|$text|" + ($script:SayAs | ConvertTo-Json -Compress -Depth 4)
    $sha = [Security.Cryptography.SHA1]::Create()
    $hash = ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($keyText)) | ForEach-Object { $_.ToString('x2') }) -join ''
    $cached = Join-Path $ttsCache "$hash.wav"
    if (-not (Test-Path -LiteralPath $cached)) { Speak-Azure -Text $text -Voice $Voice -OutFile $cached -Rate $AzureRate }
    Copy-Item -LiteralPath $cached $wav -Force
}
# Seconds from the start of a WAV (16-bit PCM, data at byte 44) to the end of the speech in it.
function SpeechEnd($wav) {
    $bytes = [IO.File]::ReadAllBytes($wav)
    $byteRate = [BitConverter]::ToInt32($bytes, 28)
    $i = $bytes.Length - 2
    while ($i -ge 44 -and [Math]::Abs([int][BitConverter]::ToInt16($bytes, $i)) -lt 700) { $i -= 2 }
    [Math]::Max(0.0, ($i - 44) / $byteRate)
}
if (-not $NoVoice) {
    if ($azure) { . (Join-Path $here 'azure_tts.ps1') }
    else {
        $tts = New-Object System.Speech.Synthesis.SpeechSynthesizer
        $tts.SelectVoice($Voice)
        $tts.Rate = $Rate
    }
    "voice: $Voice"
}
else { 'voice: none' }
$ffmpeg = $FFmpeg
if (-not $ffmpeg) {
    $ffmpeg = Get-ChildItem 'C:\ffmpeg-*' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ffmpeg) { $ffmpeg = (Get-Command ffmpeg).Source }
"ffmpeg: $ffmpeg"
if (-not ((& $ffmpeg -hide_banner -filters 2>&1 | Out-String) -match '\bxfade\b')) {
    throw "This ffmpeg has no xfade filter (it needs ffmpeg 4.3 or later): $ffmpeg"
}
$inv = [Globalization.CultureInfo]::InvariantCulture
function Num($x) { ([double]$x).ToString('0.###', $inv) }

# The joins: a dip to black after the cover and before the closing slide, dissolves elsewhere.
$count = $order.Count
$joinType = @{}; $joinLen = @{}
for ($k = 1; $k -lt $count; $k++) {
    if ((IsCard $order[$k - 1]) -or (IsCard $order[$k])) { $joinType[$k] = 'fadeblack'; $joinLen[$k] = $Dip }
    else {
        $ra = ($script:steps | Where-Object { $_.Id -eq $order[$k - 1] }).Rows
        $rb = ($script:steps | Where-Object { $_.Id -eq $order[$k] }).Rows
        if ($ra -ge 0 -and $ra -eq $rb) { $joinType[$k] = 'wipeleft'; $joinLen[$k] = $Wipe }
        else { $joinType[$k] = 'fade'; $joinLen[$k] = $Dissolve }
    }
    "join $($order[$k - 1]) -> $($order[$k]): $($joinType[$k]) $($joinLen[$k]) s"
}
$fadeInFirst = 0.8; $endHold = 1.5
# Each step: its join in, a breath, the voice (or the reading time), a pause, its join out.
$args2 = @('-hide_banner', '-loglevel', 'error', '-y')
$durs = @(); $leads = @(); $voiceIdx = @{}; $videoIdx = @{}
$idx = 0
for ($n = 0; $n -lt $count; $n++) {
    $id = $order[$n]
    $frame = Join-Path $frames ('{0:D2}_{1}.png' -f $n, $id)
    $in = if ($n -eq 0) { $fadeInFirst } else { $joinLen[$n] }
    $out = if ($n -eq $count - 1) { $endHold } else { $joinLen[$n + 1] }
    $lead = $in + 0.3; $tail = 0.8
    if ($n -eq 0) { $lead += $CoverIntro }
    $text = NoCues (Text $id).text
    if ($NoVoice) {
        # About 2.6 words a second: time to read the subtitle.
        $words = @($text -split '\s+' | Where-Object { $_ }).Count
        $secs = [Math]::Max(2.5, $words / 2.6)
        # The cover and closing slides show no subtitle: a short, fixed time.
        if ($id -eq 'title') { $secs = 1.5 } elseif ($id -eq 'end') { $secs = 4.5 } elseif ($id -like 'sec_*') { $secs = 2.5 }
        $wav = $null
    }
    else {
        $wav = Join-Path $Work ('{0:D2}.wav' -f $n)
        Say $text $wav
        $fs = [IO.File]::OpenRead($wav); $br = New-Object IO.BinaryReader $fs; $fs.Position = 28; $rate = $br.ReadInt32(); $br.Close()
        $secs = ((Get-Item $wav).Length - 44) / $rate
    }
    $dur = [Math]::Round([Math]::Max($lead + $secs + $tail + $out, 4.5), 2)
    $durs += $dur; $leads += $lead
    if ($partFrames.ContainsKey($n)) {
        # When each part starts: where its cue is spoken (the speech of the text before the cue,
        # spoken on its own), or without a voice, its share of the words.
        $cues = Cues (Text $id).text
        $starts = @()
        for ($k = 0; $k -lt $cues.Count; $k++) {
            $pos = $cues[$k].Pos
            if ($pos -eq 0) { $starts += 0.0; continue }
            $before = $text.Substring(0, $pos).Trim()
            if ($wav) {
                $pw = Join-Path $Work ('{0:D2}_cue{1}.wav' -f $n, $k)
                Say $before $pw
                $t = SpeechEnd $pw
            }
            else {
                $all = @($text -split '\s+' | Where-Object { $_ }).Count
                $t = $secs * @($before -split '\s+' | Where-Object { $_ }).Count / [Math]::Max(1, $all)
            }
            $starts += [Math]::Min($dur - 0.5, $lead + $t)
        }
        "  cues at $(($starts | ForEach-Object { Num $_ }) -join ', ') s"
        $q = { param($x) "file '" + ($x -replace '\\', '/') + "'" }
        $lines = @('ffconcat version 1.0')
        $pf = $partFrames[$n]
        for ($k = 0; $k -lt $pf.Count; $k++) {
            $end = if ($k -lt $pf.Count - 1) { $starts[$k + 1] } else { $dur + 1 }
            $lines += (& $q $pf[$k]); $lines += "duration $(Num ([Math]::Max(0.034, $end - $starts[$k])))"
        }
        $lines += (& $q $pf[-1])     # the concat reader ignores the last duration
        $listFile = Join-Path $Work ('{0:D2}_cues.txt' -f $n)
        [IO.File]::WriteAllLines($listFile, $lines)
        # Made into a clip of its own first: many image-list inputs in the one big join ran ffmpeg
        # out of memory.
        $clip = Join-Path $Work ('{0:D2}_cues.mp4' -f $n)
        & $ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i $listFile -vf 'fps=30,format=yuv420p' -t (Num $dur) -c:v libx264 -preset veryfast -crf 12 -tune stillimage $clip
        if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed on the cues of $id" }
        $args2 += @('-i', $clip)
    }
    elseif ($zooms.ContainsKey($n)) {
        $zf = $zooms[$n]
        $q = { param($x) "file '" + ($x -replace '\\', '/') + "'" }
        $zs = $in + 0.3
        $lines = @('ffconcat version 1.0', (& $q $frame), "duration $(Num $zs)")
        foreach ($z in $zf) { $lines += (& $q $z); $lines += 'duration 0.0333333' }
        $lines += (& $q $zf[-1]); $lines += "duration $(Num ($dur - $zs - $zoomTime + 1))"
        $lines += (& $q $zf[-1])     # the concat reader ignores the last duration
        $listFile = Join-Path $Work ('{0:D2}_zoom.txt' -f $n)
        [IO.File]::WriteAllLines($listFile, $lines)
        $args2 += @('-f', 'concat', '-safe', '0', '-i', $listFile)
    }
    else { $args2 += @('-loop', '1', '-framerate', '30', '-t', (Num $dur), '-i', $frame) }
    $videoIdx[$n] = $idx; $idx++
    if ($wav) { $args2 += @('-i', $wav); $voiceIdx[$n] = $idx; $idx++ }
    "step $id $(Num $dur)s"
}
if (-not $NoVoice -and -not $azure) { $tts.Dispose() }
if ($musicOn) { $args2 += @('-stream_loop', '-1', '-i', $Music); $musicIdx = $idx; $idx++ }

# Video: xfade joins. Each join starts $joinLen before the end of what is joined so far.
$f = @()
for ($n = 0; $n -lt $count; $n++) {
    $f += "[$($videoIdx[$n]):v]fps=30,format=yuv420p,setsar=1,trim=0:$(Num $durs[$n]),settb=AVTB,setpts=PTS-STARTPTS[v$n]"
}
$length = $durs[0]; $vPrev = 'v0'
for ($k = 1; $k -lt $count; $k++) {
    $f += "[$vPrev][v$k]xfade=transition=$($joinType[$k]):duration=$(Num $joinLen[$k]):offset=$(Num ($length - $joinLen[$k]))[xv$k]"
    $vPrev = "xv$k"
    $length += $durs[$k] - $joinLen[$k]
}
$total = $length
$f += "[$vPrev]fade=t=in:st=0:d=$(Num $fadeInFirst),fade=t=out:st=$(Num ($total - 1.2)):d=1.2[vout]"

# Audio: the voices, each delayed to its place, joined with the same overlaps; the music under it.
$mix = $null
if (-not $NoVoice) {
    for ($n = 0; $n -lt $count; $n++) {
        $ms = [int]($leads[$n] * 1000)
        $f += "[$($voiceIdx[$n]):a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,adelay=$ms|$ms,apad,atrim=0:$(Num $durs[$n]),asetpts=PTS-STARTPTS[a$n]"
    }
    $aPrev = 'a0'
    for ($k = 1; $k -lt $count; $k++) {
        $f += "[$aPrev][a$k]acrossfade=d=$(Num $joinLen[$k]):c1=tri:c2=tri[xa$k]"
        $aPrev = "xa$k"
    }
    $mix = $aPrev
}
if ($musicOn) {
    # The music alone at $MusicVolume, then down to $MusicUnderVoice over the second before the
    # narration starts (where it would start, without a voice), and there for the rest.
    $t1 = $leads[0]; $t0 = [Math]::Max(0.0, $t1 - 1.0)
    $hi = Num $MusicVolume; $lo = Num $MusicUnderVoice
    $level = "if(lt(t,$(Num $t0)),$hi,if(lt(t,$(Num $t1)),$hi-($hi-$lo)*(t-$(Num $t0))/$(Num ($t1 - $t0)),$lo))"
    $f += "[$($musicIdx):a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,atrim=0:$(Num $total),asetpts=PTS-STARTPTS,volume='$level':eval=frame,afade=t=in:st=0:d=2,afade=t=out:st=$(Num ($total - 3)):d=3[music]"
    if ($mix) {
        $f += "[$mix][music]amix=inputs=2:duration=first:normalize=0[amixed]"
        $mix = 'amixed'
    }
    else { $mix = 'music' }
}
if (-not $mix) { $f += "anullsrc=r=48000:cl=stereo,atrim=0:$(Num $total)[silence]"; $mix = 'silence' }
$f += "[$mix]afade=t=out:st=$(Num ($total - 1.2)):d=1.2[aout]"

$script = Join-Path $Work 'filter.txt'
[IO.File]::WriteAllText($script, ($f -join ";`n"))
$suffix = $(if ($Zoom) { '_zoom' } else { '' }) + $(if ($NoVoice) { '_novoice' } else { '' })
$outFile = Join-Path $here ("BulkNotes_Demo_{0}{1}.mp4" -f $Lang.ToUpper(), $suffix)
# Newer ffmpeg reads an option's value from a file with -/option; older ones had -filter_complex_script.
$fromFile = if ((& $ffmpeg -hide_banner -h long 2>&1 | Out-String) -match 'filter_complex_script') { '-filter_complex_script' } else { '-/filter_complex' }
$args2 += @($fromFile, $script, '-map', '[vout]', '-map', '[aout]', '-t', (Num $total),
    '-c:v', 'libx264', '-preset', 'medium', '-tune', 'stillimage', '-crf', '18', '-r', '30',
    '-c:a', 'aac', '-b:a', '160k', '-movflags', '+faststart', $outFile)
& $ffmpeg @args2
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }

# The YouTube description (the standard, section 12), with each step's question ('q' in
# narration.json) at the time its step is fully on screen in this video: the end of the join into
# it, rounded up. Only for the voiced video, whose times differ from the test's. English is
# youtubetext.txt, other languages youtubetext_<lang>.txt.
if (-not $NoVoice) {
    $yt = $narr.youtube.$Lang
    if (-not $yt) { "narration.json has no youtube text for '$Lang': no youtubetext written" }
    else {
        $lines = @($yt.about, '', $yt.download, $yt.link, '')
        $at = $durs[0]
        for ($k = 1; $k -lt $count; $k++) {
            $q = (Text $order[$k]).q
            if ($q) { $sec = [int][Math]::Ceiling($at); $lines += ('{0}:{1:D2} {2}' -f [int][Math]::Floor($sec / 60), ($sec % 60), $q) }
            $at += $durs[$k] - $joinLen[$k]
        }
        $lines += ''
        $lines += @($yt.credits)
        $ytFile = Join-Path $here $(if ($Lang -eq 'en') { 'youtubetext.txt' } else { "youtubetext_$Lang.txt" })
        [IO.File]::WriteAllText($ytFile, (($lines -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding $true))
        "written $ytFile"
    }
}
"written $outFile ($(Num $total)s)"
