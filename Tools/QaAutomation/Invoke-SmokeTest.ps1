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

# Uses a fresh filename (remote and local) on every call. A fixed, reused
# filename created a write/read race between back-to-back dumps - a dump
# taken right after typing text could pull a stale or partially-written copy
# of the PREVIOUS dump, making a just-typed field look untouched even though
# the tap and text input both landed correctly.
function Get-UiDump {
    $script:DumpCounter++
    $remote = "/sdcard/qa_dump_$($script:DumpCounter).xml"
    $local = Join-Path $OutDir "_dump_$($script:DumpCounter).xml"

    & $Adb shell uiautomator dump $remote *>$null
    & $Adb pull $remote $local *>$null
    & $Adb shell rm $remote *>$null

    if (-not (Test-Path $local)) { return $null }
    $result = [xml](Get-Content $local -Raw -Encoding UTF8)
    Remove-Item $local -ErrorAction SilentlyContinue
    return $result
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
    param([Parameter(Mandatory)]$Xml, [Parameter(Mandatory)][string]$Text, [switch]$Contains)
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

# Checks the accumulated logcat buffer (since Clear-Logcat was last called) for
# a fatal Java crash for our package, and returns the offending lines if any.
function Get-CrashSinceMark {
    $log = & $Adb logcat -d -v brief 2>$null
    $crashLines = $log | Select-String -Pattern "FATAL EXCEPTION", "AndroidRuntime: Process: $AppPackage"
    return $crashLines
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
        $first = ($crashes | Select-Object -First 1).ToString()
        Add-Result -Name $Name -Status "FAIL" -Detail "Crash in logcat: $first" -Screenshot $shot
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
        Wait-AndTapText -Text "Volver" -Contains -TimeoutSec 3 | Out-Null
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
