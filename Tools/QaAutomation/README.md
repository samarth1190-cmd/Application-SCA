# QA Automation

`Invoke-SmokeTest.ps1` is an end-to-end smoke test for the app, driven over
`adb` against a connected Android device/tablet. It starts from a completely
clean app-data state and walks every top-level screen and audit mode, taking
a screenshot at each stop and watching logcat for crashes, then writes a
pass/fail report.

This is a **breadth** smoke test, not an exhaustive content review: it opens
every mode/page at least once and confirms it renders without crashing. It
does not click through every single step of every audit (that's a content
review, not an app-health check, and would take hours).

## Running it

```powershell
cd Tools\QaAutomation
powershell -ExecutionPolicy Bypass -File .\Invoke-SmokeTest.ps1
```

Requires a device connected over `adb` (`adb devices -l` should show it) and
the tablet's screen unlocked. The run takes 5-10 minutes.

Results land in `_QA_Results/<timestamp>/` at the repo root (gitignored):
- `report.md` — a table of every check with PASS/FAIL/WARN and a screenshot reference
- `results.csv` — the same data, for spreadsheet review
- numbered `.png` screenshots at every checkpoint

## Parameters

- `-Username` / `-Password` — login credentials (default: the built-in offline test account `admin`/`admin`)
- `-SkipClear` — skip wiping app data at the start (keeps an existing login session instead of starting fresh)

## How it finds things on screen

Elements are found by their visible text via `adb shell uiautomator dump`,
not hardcoded pixel coordinates — this survives portrait/landscape rotation
and layout changes that would break coordinate-based tapping. See the
comments in `Get-NodeCenter`, `Wait-AndTapText`, and `Set-EditTextWithRetry`
for the specific real-device quirks this had to work around (a top-edge tap
dead zone under the status bar, a stale-dump race on reused filenames, the
soft keyboard sometimes owning the uiautomator focus instead of the app).

## Extending it

To add a new flow, follow the existing pattern: `Wait-AndTapText` into the
mode, call `Enter-ModelMotorAndStart` if it has a model/engine picker,
`Checkpoint` after any meaningful navigation, and `Return-ToAuditModePage`
to get back to the hub before the next flow.
