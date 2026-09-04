<#
.SYNOPSIS
    End-to-end smoke test for Aplicacion_SCA, driven over adb against a connected
    Android device/tablet. Walks every top-level screen and audit mode from a
    completely clean app-data state, taking a screenshot at each stop and
    watching logcat for crashes, then writes a pass/fail report.

.DESCRIPTION
    Finds UI elements by their visible text via "adb shell uiautomator dump"
    instead of hardcoded pixel coordinates, so it survives portrait/landscape
    rotation and layout changes that break coordinate-based tapping.

    This is a breadth smoke test, not an exhaustive content review: it visits
    every mode/page at least once and confirms it renders without crashing,
    it does not click through every single audit step of every mode (that
    would take hours and is a content review, not an app-health check).

.PARAMETER SkipClear
    Skip "pm clear" at the start (keeps existing app data / login session).
    Default is to start from a completely clean install-like state.

.PARAMETER Username / -Password
    Login credentials. Defaults to the built-in offline test account (admin/admin).

.EXAMPLE
    .\Invoke-SmokeTest.ps1
    Full clean run: wipes app data, walks every screen, writes a report under
    .\_QA_Results\<timestamp>\.
#>

param(
    [string]$AppPackage = "com.companyname.aplicacion_sca",
    [string]$Adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe",
    [string]$Username = "admin",
    [string]$Password = "admin",
    [switch]$SkipClear
)

# Deliberately NOT "Stop": adb/monkey write routine chatter to stderr as part
# of normal operation, and PowerShell 5.1 turns that into a terminating
# NativeCommandError under ErrorActionPreference=Stop even on success. This
# script does its own explicit pass/fail detection instead (Test-AppAlive,
# Get-CrashSinceMark), so let native command noise pass through harmlessly.
$ErrorActionPreference = "Continue"

# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$OutDir = Join-Path $RepoRoot "_QA_Results\$Stamp"
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$script:StepNumber = 0
$script:Results = New-Object System.Collections.Generic.List[object]

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Add-Result {
    param(
        [string]$Name,
        [ValidateSet("PASS", "FAIL", "WARN")]
        [string]$Status,
        [string]$Detail = "",
        [string]$Screenshot = ""
    )
    $script:Results.Add([PSCustomObject]@{
        Step       = ++$script:StepNumber
        Name       = $Name
        Status     = $Status
        Detail     = $Detail
        Screenshot = $Screenshot
    })
    $color = switch ($Status) { "PASS" { "Green" }; "FAIL" { "Red" }; default { "Yellow" } }
    Write-Host "  [$Status] $Name $(if ($Detail) { "- $Detail" })" -ForegroundColor $color
}

# ---------------------------------------------------------------------------
# adb / UI-tree helpers
# ---------------------------------------------------------------------------

$script:DumpCounter = 0
$script:ConsecutiveDumpFailures = 0

# Uses a fresh filename (remote and local) on every call. A fixed, reused
# filename created a write/read race between back-to-back dumps - a dump
# taken right after typing text could pull a stale or partially-written copy
# of the PREVIOUS dump, making a just-typed field look untouched even though
# the tap and text input both landed correctly.
#
# Also aborts the whole run fast if the device drops off adb mid-run (this
# tablet has done that spontaneously more than once): without this, every
# Wait-AndTapText poll loop would just silently fail against no device and
# grind through its full timeout doing nothing, over and over, looking like
# the script "hung" for minutes instead of clearly failing in seconds.
function Get-UiDump {
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $script:DumpCounter++
        $remote = "/sdcard/qa_dump_$($script:DumpCounter).xml"
        $local = Join-Path $OutDir "_dump_$($script:DumpCounter).xml"

        & $Adb shell uiautomator dump $remote *>$null
        & $Adb pull $remote $local *>$null
        & $Adb shell rm $remote *>$null

        if (Test-Path $local) {
            try {
                # A dump caught mid screen-transition can pull back truncated
                # or otherwise malformed XML - [xml] throws a terminating
                # parse error on that, which nothing downstream expects (the
                # whole point of returning $null on failure is to let callers
                # just retry the poll), so it's caught here rather than left
                # to blow up the run.
                $result = [xml](Get-Content $local -Raw -Encoding UTF8)
                Remove-Item $local -ErrorAction SilentlyContinue
                $script:ConsecutiveDumpFailures = 0
                return $result
            } catch {
                Remove-Item $local -ErrorAction SilentlyContinue
            }
        }
        Start-Sleep -Milliseconds 300
    }

    $script:ConsecutiveDumpFailures++
    if ($script:ConsecutiveDumpFailures -ge 5) {
        $stillThere = & $Adb devices -l 2>$null | Select-String "device product:"
        if (-not $stillThere) {
            $script:Results | Export-Csv -Path (Join-Path $OutDir "results.csv") -NoTypeInformation
            throw "Device disconnected from adb mid-run (5 consecutive dump failures). Reconnect the tablet and re-run - partial results were saved to $OutDir."
        }
    }
    return $null
}

function Get-NodeCenter {
    param($Node)
    if ($Node.bounds -match '\[(\d+),(\d+)\]\[(\d+),(\d+)\]') {
        $x1 = [int]$Matches[1]; $y1 = [int]$Matches[2]
        $x2 = [int]$Matches[3]; $y2 = [int]$Matches[4]
        $centerY = [int](($y1 + $y2) / 2)

        # A button whose bounds sit very close to the top of the screen (e.g.
        # a "<- Volver" back arrow tucked under the status bar) can have taps
        # at its vertical center silently swallowed - observed live: y=38
        # (the true center) did nothing, y=55 (still inside the same button,
        # just lower) worked. Bias toward the bottom of the bounds for any
        # button whose top edge is within the status-bar danger zone.
        if ($y1 -lt 70) {
            $centerY = $y2 - [int](($y2 - $y1) * 0.15)
        }

        return @{ X = [int](($x1 + $x2) / 2); Y = $centerY }
    }
    return $null
}

function Find-NodeByText {
    # $Xml is deliberately NOT [Parameter(Mandatory)] - Get-UiDump can
    # transiently return $null (a dump/pull hiccup), and a Mandatory
    # parameter rejects $null at binding time before this function's own
    # null-check below ever gets a chance to run, turning a single missed
    # dump into a hard error instead of "just try again next poll".
    param($Xml, [Parameter(Mandatory)][string]$Text, [switch]$Contains)
    if (-not $Xml) { return $null }
    $nodes = $Xml.SelectNodes("//node[@text]")
    foreach ($n in $nodes) {
        $t = $n.text
        if ([string]::IsNullOrEmpty($t)) { continue }
        if ($Contains) { if ($t.ToUpperInvariant().Contains($Text.ToUpperInvariant())) { return $n } }
        else { if ($t.ToUpperInvariant() -eq $Text.ToUpperInvariant()) { return $n } }
    }
    return $null
}

# Waits up to $TimeoutSec for text to appear anywhere on screen, tapping it as
# soon as found. Returns $true/$false. Set -NoTap to just wait/check presence.
function Wait-AndTapText {
    param(
        [Parameter(Mandatory)][string]$Text,
        [switch]$Contains,
        [switch]$NoTap,
        [int]$TimeoutSec = 15,
        [int]$PollMs = 500
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $xml = Get-UiDump
        $node = Find-NodeByText -Xml $xml -Text $Text -Contains:$Contains
        if ($node) {
            if (-not $NoTap) {
                $c = Get-NodeCenter $node
                if ($c) {
                    & $Adb shell input tap $c.X $c.Y *>$null
                    Start-Sleep -Milliseconds 600
                }
            }
            return $true
        }
        Start-Sleep -Milliseconds $PollMs
    }
    return $false
}

# Finds the EditText matching -IsPassword, taps it, and types $Value - then
# verifies the field's text actually changed from its hint before returning,
# retrying the whole tap+type on failure. Two real-device quirks made a
# single naive attempt unreliable:
#  1. Right after a heavy page transition, a tap can land before the view is
#     truly ready to receive touch/focus, even though its bounds already
#     look correct in the dump.
#  2. While the soft keyboard is up, "uiautomator dump" can report the
#     keyboard's OWN window instead of the app's (0 EditText nodes found,
#     even though the field visibly still exists) - so the keyboard is
#     dismissed with Back before ever reading the field back.
function Set-EditTextWithRetry {
    param(
        [Parameter(Mandatory)][bool]$IsPassword,
        [Parameter(Mandatory)][string]$Value,
        [int]$MaxAttempts = 3
    )
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $xml = Get-UiDump
        $field = $xml.SelectNodes("//node[@class='android.widget.EditText']") |
            Where-Object { ($_.password -eq "true") -eq $IsPassword } | Select-Object -First 1
        if (-not $field) { Start-Sleep -Milliseconds 800; continue }

        $c = Get-NodeCenter $field
        & $Adb shell input tap $c.X $c.Y *>$null
        Start-Sleep -Milliseconds 800
        & $Adb shell input text $Value *>$null
        Start-Sleep -Milliseconds 600
        & $Adb shell input keyevent 4 *>$null # Back: dismiss the keyboard so the next dump reliably targets the app's window
        Start-Sleep -Milliseconds 600

        $verifyXml = Get-UiDump
        $verifyField = $verifyXml.SelectNodes("//node[@class='android.widget.EditText']") |
            Where-Object { ($_.password -eq "true") -eq $IsPassword } | Select-Object -First 1
        if ($verifyField -and $verifyField.text -ne $verifyField.hint) {
            return $true
        }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Save-Screenshot {
    param([string]$Name)
    $remote = "/sdcard/qa_$Name.png"
    $local = Join-Path $OutDir "$($script:StepNumber.ToString('00'))_$Name.png"
    & $Adb shell screencap -p $remote *>$null
    & $Adb pull $remote $local *>$null
    return $local
}

function Test-AppAlive {
    $procId = & $Adb shell pidof $AppPackage 2>$null
    return -not [string]::IsNullOrWhiteSpace($procId)
}

function Get-ForegroundActivity {
    $out = & $Adb shell dumpsys activity activities 2>$null
    $line = $out | Select-String "topResumedActivity" | Select-Object -First 1
    return "$line"
}

# Relaunches the app if it's not alive/foregrounded (used after a crash so the
# rest of the walk can still attempt to continue and surface further issues).
function Restart-AppIfDead {
    if (-not (Test-AppAlive)) {
        Write-Step "App process is gone - relaunching to continue the walk"
        & $Adb shell monkey -p $AppPackage -c android.intent.category.LAUNCHER 1 *>$null
        Start-Sleep -Seconds 3
    }
}

# Checks the accumulated logcat buffer (since Clear-Logcat was last called)
# for a fatal Java crash for OUR package specifically, and returns the
# offending lines if any. This is a shared tablet - other apps (Chrome, Gmail,
# system UI) crashing independently would also write "FATAL EXCEPTION" to the
# same log, so a bare "FATAL EXCEPTION" match isn't enough: each crash block
# is a few lines ("FATAL EXCEPTION: main" then "Process: <pkg>, PID: <n>"
# shortly after), so this only counts a match where the package name actually
# shows up in that block.
function Get-CrashSinceMark {
    $log = & $Adb logcat -d -v brief 2>$null
    $hits = $log | Select-String -Pattern "FATAL EXCEPTION" -Context 0, 6
    $ours = foreach ($hit in $hits) {
        $block = @($hit.Line) + @($hit.Context.PostContext)
        if (($block -join "`n") -match [regex]::Escape($AppPackage)) { $hit }
    }
    return $ours
}

function Clear-Logcat {
    & $Adb logcat -c *>$null
}

# One checkpoint = screenshot + alive check + crash-log check, called after
# every navigation step so a failure can be pinned to the action that caused it.
function Checkpoint {
    param([string]$Name)
    $shot = Save-Screenshot -Name ($Name -replace '[^a-zA-Z0-9_-]', '_')
    $alive = Test-AppAlive
    $crashes = Get-CrashSinceMark

    if (-not $alive) {
        Add-Result -Name $Name -Status "FAIL" -Detail "App process died" -Screenshot $shot
        Restart-AppIfDead
    } elseif ($crashes) {
        # Persist the full crash block (not just its first line) next to the
        # screenshot so a real hit is actually debuggable from the report
        # instead of a one-line summary.
        $crashLogName = ($Name -replace '[^a-zA-Z0-9_-]', '_') + "_crash.log"
        $crashLogPath = Join-Path $OutDir $crashLogName
        $first = $crashes | Select-Object -First 1
        $fullBlock = @($first.Line) + @($first.Context.PostContext)
        $fullBlock -join "`n" | Out-File -FilePath $crashLogPath -Encoding UTF8
        Add-Result -Name $Name -Status "FAIL" -Detail "Crash in logcat for $AppPackage - see $crashLogName" -Screenshot $shot
    } else {
        Add-Result -Name $Name -Status "PASS" -Screenshot $shot
    }
    Clear-Logcat
}

# Walks back to AuditModePage using the "<- Volver" back arrow every inner
# page has, positioned right under the status bar - Get-NodeCenter already
# biases the tap away from that top-edge dead zone. Volver can pop a "Really
# leave?" confirmation - some pages use a custom in-app overlay, others a
# real MAUI DisplayAlert - either way its confirm button reads exactly
# "Si, Salir" (accented i - matched here by the ", Salir" tail only, since
# this script's own source can't safely contain non-ASCII text; see the
# encoding note where PlantRegistry.cs word lists were made ASCII-only for
# the same reason). The comma is the important part: every inner page ALSO
# has its OWN permanent "X Salir" exit button in the corner with no comma,
# so matching on ", Salir" (etc.) instead of a bare "Salir" Contains search
# avoids tapping that unrelated button and opening a second, unwanted
# confirmation.
function Return-ToAuditModePage {
    param([int]$MaxHops = 8)
    for ($i = 0; $i -lt $MaxHops; $i++) {
        if (Wait-AndTapText -Text "C-DPV" -NoTap -TimeoutSec 2) { return $true }

        # A page can be sitting behind a plain info dialog (e.g. ResultsPage's
        # "Exito / Auditoria finalizada y guardada correctamente" success
        # alert, seen when a submission that used to fail gracefully due to
        # an empty SharePoint ResultsListId now succeeds) - nothing below
        # this dismisses a bare "OK" alert, so without this the loop just
        # spins against it until MaxHops runs out.
        Wait-AndTapText -Text "OK" -TimeoutSec 1 | Out-Null

        $tappedVolver = Wait-AndTapText -Text "Volver" -Contains -TimeoutSec 3
        if (-not $tappedVolver) {
            # Some pages (RRUPage) only expose the red "X Salir"/"X Exit" full
            # audit-abort button in their header, no lighter "Volver" - that's
            # still a valid way back for a breadth smoke test, it just needs
            # its own confirmation handled below like any other exit.
            Wait-AndTapText -Text "Salir" -Contains -TimeoutSec 2 | Out-Null
            Wait-AndTapText -Text "Exit" -Contains -TimeoutSec 1 | Out-Null
        }
        Start-Sleep -Milliseconds 900
        Wait-AndTapText -Text ", Salir" -Contains -TimeoutSec 2 | Out-Null
        Wait-AndTapText -Text ", Exit" -Contains -TimeoutSec 1 | Out-Null
        Wait-AndTapText -Text ", Quitter" -Contains -TimeoutSec 1 | Out-Null
        Wait-AndTapText -Text ", Verlassen" -Contains -TimeoutSec 1 | Out-Null
        Start-Sleep -Milliseconds 600
    }
    return (Wait-AndTapText -Text "C-DPV" -NoTap -TimeoutSec 2)
}

# ---------------------------------------------------------------------------
# Pre-flight
# ---------------------------------------------------------------------------

Write-Step "Checking device connection"
$devices = & $Adb devices -l
if (-not ($devices | Select-String "device product:")) {
    throw "No Android device detected over adb. Connect the tablet and re-run."
}
Write-Host ($devices -join "`n")

# The tablet auto-rotates freely between portrait and landscape during normal
# use. If a rotation happens between a uiautomator dump and the tap that acts
# on it, the computed tap coordinates are for the pre-rotation layout and can
# miss the real on-screen element entirely (observed: taps landing on empty
# space, login fields never getting focused). Lock orientation for the run.
Write-Step "Locking device orientation for the run (was auto-rotating, causing stale tap coordinates)"
& $Adb shell settings put system accelerometer_rotation 0 *>$null
& $Adb shell settings put system user_rotation 0 *>$null
Start-Sleep -Milliseconds 500

# The tablet has disconnected from adb mid-run more than once, and it
# clusters around the long TTS-playback waits (up to 150s with zero touch
# input) rather than happening randomly - consistent with the screen timing
# out and the device dropping into a deeper power-saving state that also
# takes USB/adb down with it. "svc power stayon usb" keeps the screen (and
# with it, apparently, the adb connection) awake for as long as USB stays
# plugged in, which a real auditor's tablet wouldn't need but this
# unattended automated run does.
Write-Step "Keeping screen awake while USB-connected (disconnects were clustering around long no-touch waits)"
& $Adb shell svc power stayon usb *>$null

Clear-Logcat

if (-not $SkipClear) {
    Write-Step "Clearing app data (clean-slate run)"
    & $Adb shell pm clear $AppPackage *>$null
    Start-Sleep -Seconds 1
}

Write-Step "Launching app"
& $Adb shell monkey -p $AppPackage -c android.intent.category.LAUNCHER 1 *>$null
Start-Sleep -Seconds 3
Checkpoint -Name "app_launch"

# ---------------------------------------------------------------------------
# Onboarding: language screen -> EMPEZAR -> login -> dismiss notices
# ---------------------------------------------------------------------------

Write-Step "Tapping EMPEZAR (downloads Usuarios/Vehiculos)"
if (-not (Wait-AndTapText -Text "EMPEZAR" -TimeoutSec 10)) {
    Add-Result -Name "Find EMPEZAR button" -Status "FAIL" -Detail "Not found on start screen"
} else {
    Add-Result -Name "Tap EMPEZAR" -Status "PASS"
}

Write-Step "Waiting for login screen to load (data download)"
$loginReady = Wait-AndTapText -Text "ENTRAR" -NoTap -TimeoutSec 20
Add-Result -Name "Login screen loaded" -Status $(if ($loginReady) { "PASS" } else { "FAIL" })
Checkpoint -Name "login_screen"

if ($loginReady) {
    # The dump can report "ENTRAR" as soon as the page's layout exists, but
    # entrance animations / handler attachment may still be settling for a
    # moment after that - give it a beat before the first interaction.
    # Set-EditTextWithRetry below covers the rest of this class of flakiness
    # with its own verify-and-retry loop.
    Start-Sleep -Seconds 2
    Write-Step "Logging in as $Username"

    $userOk = Set-EditTextWithRetry -IsPassword $false -Value $Username
    $passOk = Set-EditTextWithRetry -IsPassword $true -Value $Password

    if ($userOk -and $passOk) {
        # Keyboard is already dismissed (Set-EditTextWithRetry does this after
        # the password field too).
        $tapped = Wait-AndTapText -Text "ENTRAR" -TimeoutSec 5
        Start-Sleep -Seconds 2
        Add-Result -Name "Submit login form" -Status $(if ($tapped) { "PASS" } else { "FAIL" }) -Detail $(if (-not $tapped) { "ENTRAR button not found after filling form" } else { "" })
    } else {
        Add-Result -Name "Submit login form" -Status "FAIL" -Detail "Field(s) never took typed content (user ok=$userOk, pass ok=$passOk) after retries - not submitting"
    }
}

# Android's own "save password?" autofill prompt can cover the screen right
# after a login form submit - it's an OS overlay, not part of the app, but it
# blocks every subsequent Wait-AndTapText until dismissed.
Wait-AndTapText -Text "No thanks" -Contains -TimeoutSec 3 | Out-Null
Wait-AndTapText -Text "No, gracias" -Contains -TimeoutSec 2 | Out-Null

# "Bienvenido Admin / Entrar al Sistema" info popup, if shown
Wait-AndTapText -Text "Entrar al Sistema" -Contains -TimeoutSec 4 | Out-Null
# "Avisos y Novedades" notice dialog, if shown
Wait-AndTapText -Text "ENTENDIDO" -Contains -TimeoutSec 4 | Out-Null
Start-Sleep -Seconds 1
Checkpoint -Name "audit_mode_page"

$onAuditModePage = Wait-AndTapText -Text "C-DPV" -NoTap -TimeoutSec 8
Add-Result -Name "Reached AuditModePage" -Status $(if ($onAuditModePage) { "PASS" } else { "FAIL" })

if (-not $onAuditModePage) {
    Write-Host "Could not reach AuditModePage - aborting the rest of the walk." -ForegroundColor Red
    $script:Results | Export-Csv -Path (Join-Path $OutDir "results.csv") -NoTypeInformation
    & $Adb shell settings put system accelerometer_rotation 1 *>$null
& $Adb shell svc power stayon false *>$null
    return
}

# ---------------------------------------------------------------------------
# Verify every static button on AuditModePage is present
# ---------------------------------------------------------------------------

Write-Step "Verifying AuditModePage buttons"
foreach ($label in @("JAP", "C-DPV", "DPV", "FORMACI")) {
    $found = Wait-AndTapText -Text $label -Contains -NoTap -TimeoutSec 3
    Add-Result -Name "AuditModePage has '$label'" -Status $(if ($found) { "PASS" } else { "FAIL" })
}
# RRU/Plantilla Otros are conditional on SharePoint content existing - presence
# isn't a pass/fail, just informational.
$hasRRU = Wait-AndTapText -Text "RRU" -NoTap -TimeoutSec 2
Add-Result -Name "AuditModePage has 'RRU' (conditional button)" -Status $(if ($hasRRU) { "PASS" } else { "WARN" }) -Detail "Only shown if 07_RRU/RRU.xlsx exists in SharePoint"

# ---------------------------------------------------------------------------
# Helper: walk a "pick plant (if any) -> pick model+motor -> COMENZAR" flow
# ---------------------------------------------------------------------------

function Enter-ModelMotorAndStart {
    param([string]$FlowName)

    Start-Sleep -Seconds 1
    $xml = Get-UiDump
    $node = Find-NodeByText -Xml $xml -Text "STELLANTIS VIGO"
    if ($node) {
        Write-Step "$FlowName : selecting plant STELLANTIS VIGO"
        Wait-AndTapText -Text "STELLANTIS VIGO" -TimeoutSec 5 | Out-Null
        Start-Sleep -Seconds 1
    }

    # Pick the first model button under "SELECCIONE MODELO" / "SELECT MODEL"
    # and the first engine button - content-agnostic: whichever model/engine
    # labels this Excel/plant defines, take the first of each.
    $xml = Get-UiDump
    if (-not $xml) { Add-Result -Name "$FlowName : model/motor screen" -Status "FAIL" -Detail "No UI dump"; return $false }

    $modelHeader = Find-NodeByText -Xml $xml -Text "MODEL" -Contains
    $engineHeader = Find-NodeByText -Xml $xml -Text "MOTOR" -Contains
    if (-not $engineHeader) { $engineHeader = Find-NodeByText -Xml $xml -Text "ENGINE" -Contains }

    if (-not $modelHeader -or -not $engineHeader) {
        Add-Result -Name "$FlowName : model/motor screen" -Status "FAIL" -Detail "Could not locate model/engine section headers"
        return $false
    }

    # Buttons render as sibling nodes after the section header, before the next
    # header - approximate by y-position: first clickable text below the
    # model header and above the engine header is the model choice, and
    # similarly for engine.
    $allTextNodes = $xml.SelectNodes("//node[@text!='']") | ForEach-Object {
        $b = $_.bounds
        if ($b -match '\[(\d+),(\d+)\]\[(\d+),(\d+)\]') {
            [PSCustomObject]@{ Node = $_; Text = $_.text; Y = [int]$Matches[2] }
        }
    } | Sort-Object Y

    $modelY = ($allTextNodes | Where-Object { $_.Node -eq $modelHeader }).Y
    $engineY = ($allTextNodes | Where-Object { $_.Node -eq $engineHeader }).Y

    $modelChoice = $allTextNodes | Where-Object { $_.Y -gt $modelY -and $_.Y -lt $engineY } | Select-Object -First 1
    $engineChoice = $allTextNodes | Where-Object { $_.Y -gt $engineY } | Select-Object -First 1

    if ($modelChoice) {
        $c = Get-NodeCenter $modelChoice.Node
        & $Adb shell input tap $c.X $c.Y *>$null
        Start-Sleep -Milliseconds 400
        Add-Result -Name "$FlowName : pick model '$($modelChoice.Text)'" -Status "PASS"
    } else {
        Add-Result -Name "$FlowName : pick model" -Status "FAIL" -Detail "No model button found"
    }

    if ($engineChoice) {
        $c = Get-NodeCenter $engineChoice.Node
        & $Adb shell input tap $c.X $c.Y *>$null
        Start-Sleep -Milliseconds 400
        Add-Result -Name "$FlowName : pick engine '$($engineChoice.Text)'" -Status "PASS"
    } else {
        Add-Result -Name "$FlowName : pick engine" -Status "FAIL" -Detail "No engine button found"
    }

    $started = Wait-AndTapText -Text "COMENZAR" -Contains -TimeoutSec 5
    if (-not $started) { $started = Wait-AndTapText -Text "START" -Contains -TimeoutSec 5 }
    Add-Result -Name "$FlowName : tap COMENZAR/START" -Status $(if ($started) { "PASS" } else { "FAIL" })

    # Excel download can take a few seconds over the network.
    Start-Sleep -Seconds 5
    Checkpoint -Name "$($FlowName)_after_comenzar"
    return $true
}

# ---------------------------------------------------------------------------
# EstandarPage hands-free voice-command flow (v1.8.2+): drives the physical-
# fallback buttons ("Mas Detalle" / "Validar y Continuar"), which run through
# the exact same code path as the matching voice commands ("detalle"/
# "siguiente") - see _comandoForzado in EstandarPage.xaml.cs. adb cannot
# inject real speech into the mic, so this is the closest thing to an
# automated test of that flow: it exercises the state machine end to end
# (listen -> reveal detail -> keep listening -> advance step) without
# actually needing a human voice.
# ---------------------------------------------------------------------------

function Test-EstandarPageVoiceFlow {
    Write-Step "EstandarPage: voice-command flow (COMENZAR -> listen -> detail -> next), physical-fallback buttons"

    # Wrapped in try/finally: several paths below return early on a failed
    # check, and without a finally, an early return skipped the final
    # "Pausar" tap - leaving the sequence running/speaking in the background
    # for the rest of the walk, which then made unrelated later steps
    # (Test-ResultsSubmission's "Volver" navigation) fight against a live
    # confirmation dialog they weren't expecting.
    try {
        $hasDetailBtn = Wait-AndTapText -Text "DETALLE" -Contains -NoTap -TimeoutSec 3
        Add-Result -Name "EstandarPage : 'Mas Detalle' button present before starting" -Status $(if ($hasDetailBtn) { "PASS" } else { "WARN" }) -Detail "Only shown if this step has AudioAuditoria/AudioFormacion text"

        $comenzarOk = Wait-AndTapText -Text "COMENZAR" -Contains -TimeoutSec 5
        Add-Result -Name "EstandarPage : tap COMENZAR (starts hands-free sequence)" -Status $(if ($comenzarOk) { "PASS" } else { "FAIL" })
        if (-not $comenzarOk) { return }

        # Step 1's Fase can be a long paragraph - this is real TTS speech
        # taking real wall-clock time on the device speaker (plus the 0.5s
        # pause now inserted between every sentence), not something that can
        # be sped up. Give it up to 150s to finish and reach the listening
        # state (the green "Validar y Continuar" panel), which is when the
        # physical fallback buttons actually start being consumed by
        # EjecutarValidacionManual - tapping them before that does nothing.
        $listening = Wait-AndTapText -Text "VALIDAR Y CONTINUAR" -NoTap -TimeoutSec 150
        Add-Result -Name "EstandarPage : reaches listening state after speaking Fase" -Status $(if ($listening) { "PASS" } else { "FAIL" }) -Detail $(if (-not $listening) { "Listening indicator not seen within 150s" } else { "" })
        Checkpoint -Name "estandarpage_listening_state"
        if (-not $listening) { return }

        # Tap "Mas Detalle" - the physical-fallback equivalent of the voice
        # command "detalle"/"detail": should speak+reveal AudioAuditoria
        # without advancing the step or ending the listening loop.
        $detailTapped = Wait-AndTapText -Text "DETALLE" -Contains -TimeoutSec 3
        Add-Result -Name "EstandarPage : tap 'Mas Detalle' (voice 'detalle' equivalent)" -Status $(if ($detailTapped) { "PASS" } else { "FAIL" }) -Detail $(if (-not $detailTapped) { "Button not found while listening" } else { "" })
        Start-Sleep -Seconds 3
        Checkpoint -Name "estandarpage_more_detail"

        # The detail speech has to finish before listening resumes; then tap
        # "Validar y Continuar" - the physical-fallback equivalent of the
        # voice command "siguiente"/"next": should advance step 1 to step 2.
        $listeningAgain = Wait-AndTapText -Text "VALIDAR Y CONTINUAR" -NoTap -TimeoutSec 90
        if ($listeningAgain) {
            Wait-AndTapText -Text "VALIDAR Y CONTINUAR" -TimeoutSec 5 | Out-Null
            Start-Sleep -Seconds 2
            $step2 = Wait-AndTapText -Text "Paso 2" -Contains -NoTap -TimeoutSec 5
            if (-not $step2) { $step2 = Wait-AndTapText -Text "Step 2" -Contains -NoTap -TimeoutSec 3 }
            Add-Result -Name "EstandarPage : 'Validar y Continuar' advances step (voice 'siguiente' equivalent)" -Status $(if ($step2) { "PASS" } else { "FAIL" })
        } else {
            Add-Result -Name "EstandarPage : re-enter listening state after detail" -Status "FAIL" -Detail "Listening indicator not seen within 90s"
        }
        Checkpoint -Name "estandarpage_advanced_step"
    } finally {
        # Pause so the sequence doesn't keep running/speaking in the
        # background for the rest of the walk, whichever path got here.
        Wait-AndTapText -Text "PAUSAR" -Contains -TimeoutSec 3 | Out-Null
    }
}

# ---------------------------------------------------------------------------
# ResultsPage submission flow: VIN entry on MenuEstandarPage -> "Finalizar
# Auditoria" -> ResultsPage -> "Finalizar y Enviar" -> confirm dialog.
# ResultsListId is currently empty for both plants (a known, pre-existing gap
# - see CHANGELOG), so the actual SharePoint list write is expected to fail;
# what this checks is that the app handles that failure gracefully (an
# alert, not a crash) rather than whether the write itself succeeds.
# ---------------------------------------------------------------------------

function Test-ResultsSubmission {
    param([string]$FlowName)

    Write-Step "$FlowName : VIN entry -> Finalizar Auditoria -> ResultsPage submission"

    # Back out of EstandarPage to MenuEstandarPage first (the VIN field lives
    # there, not on EstandarPage).
    Wait-AndTapText -Text "Volver" -Contains -TimeoutSec 3 | Out-Null
    Start-Sleep -Milliseconds 900
    Wait-AndTapText -Text ", Salir" -Contains -TimeoutSec 2 | Out-Null
    Start-Sleep -Seconds 1

    $onMenu = Wait-AndTapText -Text "HOJA DE RUTA" -Contains -NoTap -TimeoutSec 5
    if (-not $onMenu) { $onMenu = Wait-AndTapText -Text "ROUTE SHEET" -Contains -NoTap -TimeoutSec 3 }
    if (-not $onMenu) {
        Add-Result -Name "$FlowName : back on MenuEstandarPage for VIN entry" -Status "FAIL" -Detail "Did not land back on MenuEstandarPage"
        return
    }

    # A VIN matching Vigo's chassis pattern is enough to satisfy the length
    # check in OnFinalizarAuditoriaClicked (2 letters + 6 digits minimum);
    # exact-pattern validation, if any, happens further down the flow.
    $xml = Get-UiDump
    $vinField = $xml.SelectNodes("//node[@class='android.widget.EditText']") | Select-Object -First 1
    $vinOk = $false
    if ($vinField) {
        $c = Get-NodeCenter $vinField
        & $Adb shell input tap $c.X $c.Y *>$null
        Start-Sleep -Milliseconds 600
        & $Adb shell input text "QA123456" *>$null
        Start-Sleep -Milliseconds 500
        & $Adb shell input keyevent 4 *>$null
        Start-Sleep -Milliseconds 500
        $vinOk = $true
    }
    Add-Result -Name "$FlowName : enter VIN" -Status $(if ($vinOk) { "PASS" } else { "FAIL" }) -Detail $(if (-not $vinOk) { "VIN field not found" } else { "" })

    $finalizarTapped = Wait-AndTapText -Text "FINALIZAR AUDITOR" -Contains -TimeoutSec 5
    Add-Result -Name "$FlowName : tap FINALIZAR AUDITORIA" -Status $(if ($finalizarTapped) { "PASS" } else { "FAIL" })
    Start-Sleep -Seconds 2

    $onResults = Wait-AndTapText -Text "FINALIZAR Y ENVIAR" -Contains -NoTap -TimeoutSec 8
    Add-Result -Name "$FlowName : reached ResultsPage" -Status $(if ($onResults) { "PASS" } else { "FAIL" })
    Checkpoint -Name "$($FlowName)_results_page"
    if (-not $onResults) { return }

    Wait-AndTapText -Text "FINALIZAR Y ENVIAR" -Contains -TimeoutSec 5 | Out-Null
    Start-Sleep -Milliseconds 800
    # Confirm dialog reads "Si, enviar" / "Cancelar" - matched here by the
    # comma+lowercase tail only, since this script's own source has to stay
    # ASCII-only (see the encoding note on Return-ToAuditModePage) and can't
    # hold the accented i.
    $confirmTapped = Wait-AndTapText -Text ", enviar" -Contains -TimeoutSec 5
    Add-Result -Name "$FlowName : confirm submission dialog" -Status $(if ($confirmTapped) { "PASS" } else { "WARN" }) -Detail $(if (-not $confirmTapped) { "Confirm dialog not found - VIN validation may have blocked submission first" } else { "" })

    Start-Sleep -Seconds 3
    $stillAlive = Test-AppAlive
    Add-Result -Name "$FlowName : app survives submit attempt (expected to fail gracefully, not crash)" -Status $(if ($stillAlive) { "PASS" } else { "FAIL" }) -Detail "ResultsListId is empty for both plants (known gap) - a graceful error alert here is expected, a crash is not"

    # Dismiss whatever alert appears (success or the graceful failure one) so
    # the walk can continue.
    Wait-AndTapText -Text "OK" -TimeoutSec 3 | Out-Null
    Wait-AndTapText -Text "ENTENDIDO" -Contains -TimeoutSec 2 | Out-Null
    Checkpoint -Name "$($FlowName)_after_submit_attempt"
}

# ---------------------------------------------------------------------------
# C-DPV flow (plant-aware)
# ---------------------------------------------------------------------------

Write-Step "=== Flow: C-DPV ==="
if (Wait-AndTapText -Text "C-DPV" -TimeoutSec 5) {
    Add-Result -Name "Enter C-DPV" -Status "PASS"
    if (Enter-ModelMotorAndStart -FlowName "C-DPV") {
        $onMenu = Wait-AndTapText -Text "HOJA DE RUTA" -Contains -NoTap -TimeoutSec 8
        if (-not $onMenu) { $onMenu = Wait-AndTapText -Text "ROUTE SHEET" -Contains -NoTap -TimeoutSec 3 }
        Add-Result -Name "C-DPV : reached MenuEstandarPage" -Status $(if ($onMenu) { "PASS" } else { "FAIL" })

        if ($onMenu) {
            foreach ($section in @("STATIC", "TICO")) {
                if (Wait-AndTapText -Text $section -Contains -TimeoutSec 3) {
                    Add-Result -Name "C-DPV : open '$section' section" -Status "PASS"
                    Start-Sleep -Seconds 2
                    $onStandard = Wait-AndTapText -Text "PASO" -Contains -NoTap -TimeoutSec 6
                    if (-not $onStandard) { $onStandard = Wait-AndTapText -Text "STEP" -Contains -NoTap -TimeoutSec 3 }
                    Add-Result -Name "C-DPV : EstandarPage renders (step counter visible)" -Status $(if ($onStandard) { "PASS" } else { "FAIL" })
                    Checkpoint -Name "cdpv_estandar_page"

                    if ($onStandard) {
                        Test-EstandarPageVoiceFlow
                        Test-ResultsSubmission -FlowName "C-DPV"
                    }
                    break
                }
            }
        }
    }
} else {
    Add-Result -Name "Enter C-DPV" -Status "FAIL" -Detail "Button not found"
}

# Return to AuditModePage for the next flow.
Write-Step "Returning to AuditModePage"
$backOk = Return-ToAuditModePage
Add-Result -Name "Navigate back to AuditModePage (from C-DPV)" -Status $(if ($backOk) { "PASS" } else { "FAIL" })
Checkpoint -Name "back_to_audit_mode_after_cdpv"

# ---------------------------------------------------------------------------
# DPV flow
# ---------------------------------------------------------------------------

Write-Step "=== Flow: DPV ==="
if (Wait-AndTapText -Text "DPV" -TimeoutSec 5) {
    Add-Result -Name "Enter DPV" -Status "PASS"
    Enter-ModelMotorAndStart -FlowName "DPV" | Out-Null
} else {
    Add-Result -Name "Enter DPV" -Status "FAIL" -Detail "Button not found"
}

Write-Step "Returning to AuditModePage"
$backOk = Return-ToAuditModePage
Add-Result -Name "Navigate back to AuditModePage (from DPV)" -Status $(if ($backOk) { "PASS" } else { "FAIL" })
Checkpoint -Name "back_to_audit_mode_after_dpv"

# ---------------------------------------------------------------------------
# Control Japon flow
# ---------------------------------------------------------------------------

Write-Step "=== Flow: Control Japon ==="
if (Wait-AndTapText -Text "JAP" -Contains -TimeoutSec 5) {
    Add-Result -Name "Enter Control Japon" -Status "PASS"
    Start-Sleep -Seconds 2
    Checkpoint -Name "control_japon_page"
} else {
    Add-Result -Name "Enter Control Japon" -Status "FAIL" -Detail "Button not found"
}

Write-Step "Returning to AuditModePage"
$backOk = Return-ToAuditModePage
Add-Result -Name "Navigate back to AuditModePage (from Control Japon)" -Status $(if ($backOk) { "PASS" } else { "FAIL" })
Checkpoint -Name "back_to_audit_mode_after_japon"

# ---------------------------------------------------------------------------
# Formacion SCA flow
# ---------------------------------------------------------------------------

Write-Step "=== Flow: Formacion SCA ==="
if (Wait-AndTapText -Text "FORMACI" -Contains -TimeoutSec 5) {
    Add-Result -Name "Enter Formacion SCA" -Status "PASS"
    Enter-ModelMotorAndStart -FlowName "Formacion" | Out-Null
} else {
    Add-Result -Name "Enter Formacion SCA" -Status "FAIL" -Detail "Button not found"
}

Write-Step "Returning to AuditModePage"
$backOk = Return-ToAuditModePage
Add-Result -Name "Navigate back to AuditModePage (from Formacion)" -Status $(if ($backOk) { "PASS" } else { "FAIL" })
Checkpoint -Name "back_to_audit_mode_after_formacion"

# ---------------------------------------------------------------------------
# RRU flow (conditional: button only exists if 07_RRU/RRU.xlsx is present in
# SharePoint - $hasRRU was recorded earlier from the AuditModePage button
# inventory). Goes SelectionPage -> RRUPage directly, same shape as Control
# Japon, no MenuEstandarPage in between.
# ---------------------------------------------------------------------------

Write-Step "=== Flow: RRU ==="
if ($hasRRU) {
    if (Wait-AndTapText -Text "RRU" -TimeoutSec 5) {
        Add-Result -Name "Enter RRU" -Status "PASS"
        if (Enter-ModelMotorAndStart -FlowName "RRU") {
            $onRRUPage = Wait-AndTapText -Text "VALIDAR PARADA" -Contains -NoTap -TimeoutSec 8
            Add-Result -Name "RRU : reached RRUPage" -Status $(if ($onRRUPage) { "PASS" } else { "FAIL" })
            Checkpoint -Name "rru_page"
        }
    } else {
        Add-Result -Name "Enter RRU" -Status "FAIL" -Detail "Button was present in inventory but not found now"
    }

    Write-Step "Returning to AuditModePage"
    $backOk = Return-ToAuditModePage
    Add-Result -Name "Navigate back to AuditModePage (from RRU)" -Status $(if ($backOk) { "PASS" } else { "FAIL" })
    Checkpoint -Name "back_to_audit_mode_after_rru"
} else {
    Add-Result -Name "Enter RRU" -Status "WARN" -Detail "RRU button not present in this SharePoint content - skipped"
}

# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------

$csvPath = Join-Path $OutDir "results.csv"
$script:Results | Export-Csv -Path $csvPath -NoTypeInformation

$passCount = ($script:Results | Where-Object Status -eq "PASS").Count
$failCount = ($script:Results | Where-Object Status -eq "FAIL").Count
$warnCount = ($script:Results | Where-Object Status -eq "WARN").Count

$reportPath = Join-Path $OutDir "report.md"
$lines = @()
$lines += "# SCA smoke test - $Stamp"
$lines += ""
$lines += "**$passCount passed, $failCount failed, $warnCount warnings** out of $($script:Results.Count) checks."
$lines += ""
$lines += "| # | Check | Status | Detail | Screenshot |"
$lines += "|---|---|---|---|---|"
foreach ($r in $script:Results) {
    $shotName = if ($r.Screenshot) { Split-Path -Leaf $r.Screenshot } else { "" }
    $lines += "| $($r.Step) | $($r.Name) | $($r.Status) | $($r.Detail) | $shotName |"
}
$lines -join "`n" | Out-File -FilePath $reportPath -Encoding UTF8

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  $passCount PASS / $failCount FAIL / $warnCount WARN" -ForegroundColor Cyan
Write-Host "  Report: $reportPath" -ForegroundColor Cyan
Write-Host "  Screenshots + CSV: $OutDir" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

& $Adb shell settings put system accelerometer_rotation 1 *>$null
& $Adb shell svc power stayon false *>$null
