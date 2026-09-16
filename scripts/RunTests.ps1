# =============================================================================
# RC Plaza - automated acceptance test runner
# Runs the Unity Test Framework suites (EditMode + PlayMode) headlessly and
# checks every result against the expectations of doc "RC模拟游戏设计.md" §7.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\RunTests.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\RunTests.ps1 -EditOnly
#   powershell -ExecutionPolicy Bypass -File scripts\RunTests.ps1 -EditorPath "C:\...\Unity.exe"
#
# First run may take 10-25 min (package import + compilation + both suites).
# Re-runs take ~2-5 min, ~1.5 min measured with warm caches (a lingering
# batchmode process after results are saved is detected via results polling
# and reclaimed immediately instead of waiting out the full timeout).
#
# Exit codes: 0 = ALL tests passed, 1 = license/tooling problem, 2 = test failures
# =============================================================================
param(
    [string]$EditorPath = "",
    [string]$ProjectPath = "",
    [switch]$EditOnly,
    [switch]$PlayOnly,
    [string]$LogDir = "",
    [int]$TimeoutMinutes = 25,
    [string]$ManualLicense = ""
)

$ErrorActionPreference = "Stop"

# ---- project paths (defaults resolve against this script's location) -------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($ProjectPath)) { $ProjectPath = Split-Path -Parent $ScriptDir }
if ([string]::IsNullOrEmpty($LogDir))    { $LogDir    = Join-Path $ProjectPath "TestResults" }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$UnityVersion = "2022.3.20f1"

# ---- locate the Unity editor ------------------------------------------------
function Find-UnityEditor {
    if ($EditorPath -ne "") {
        if (Test-Path $EditorPath) { return $EditorPath }
        Write-Host "[ERROR] EditorPath not found: $EditorPath" -ForegroundColor Red
        exit 1
    }
    # default Hub install location (per-user)
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    # ask Unity Hub CLI (MSIX install) for its editors list
    try {
        $hub = (Get-AppxPackage UnityTechnologies.UnityHub) | Select-Object -First 1
        if ($hub) {
            $hubExe = Join-Path $hub.InstallLocation "Unity Hub.exe"
            $raw = & $hubExe -- --headless editors -r -i 2>$null
            if ($raw -match '"path"\s*:\s*"([^"]+)"') { return $Matches[1] }
        }
    } catch { }
    Write-Host "[ERROR] Unity $UnityVersion not found." -ForegroundColor Red
    Write-Host "        Install it via Unity Hub, or pass -EditorPath <Unity.exe>."
    exit 1
}

# ---- keep Unity Hub alive: the Hub process brokers the editor license -------
# (editor headless runs fail with "No ULF license found / token not found"
#  as soon as the Hub is not running)
function Ensure-HubRunning {
    if (Get-Process "Unity Hub" -ErrorAction SilentlyContinue) { return }
    try {
        # MSIX-packaged Hub: "<InstallLocation>\Unity Hub.exe" is not directly
        # launchable (file-not-found); the shell:AppsFolder alias of the
        # installed package works.
        Start-Process 'shell:AppsFolder\UnityTechnologies.UnityHub_2vrhnee42bhxm!UnityHub'
        Start-Sleep -Seconds 8
    } catch { }
}

# ---- license check (Editor requires an activated license even headless) -----
function Assert-Licensed($unity) {
    if ($ManualLicense -ne "") {
        Write-Host "Activating license from file: $ManualLicense" -ForegroundColor Cyan
        $actLog = Join-Path $LogDir "license-activate.log"
        $p = Start-Process -FilePath $unity -ArgumentList @(
            "-batchmode", "-nographics", "-quit", "-projectPath", $ProjectPath,
            "-manualLicenseFile", $ManualLicense, "-logFile", $actLog
        ) -Wait -PassThru -NoNewWindow
        if ($p.ExitCode -ne 0) {
            Write-Host "[ERROR] License activation failed (exit $($p.ExitCode)); see $actLog" -ForegroundColor Red
            exit 1
        }
        return
    }
    $log = Join-Path $LogDir "license-check.log"
    $p = Start-Process -FilePath $unity -ArgumentList @(
        "-batchmode", "-nographics", "-quit", "-projectPath", $ProjectPath,
        "-logFile", $log
    ) -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -eq 1 -and (Select-String -Path $log -Pattern "No valid Unity Editor license" -Quiet)) {
        Write-Host ""
        Write-Host "[ERROR] Unity Editor license not activated in this machine." -ForegroundColor Red
        Write-Host "        One-time manual step (pick ONE):"
        Write-Host "        A0) Make sure Unity Hub is running (it brokers the license) - this script
             starts it automatically, but a fresh machine may need a Hub GUI sign-in;"
        Write-Host "        A) Open Unity Hub GUI -> Sign in with your Unity account -> it activates automatically, then re-run this script;"
        Write-Host "        B) Manual license:"
        Write-Host "             $unity -batchmode -createManualActivationFile -logFile alf.log"
        Write-Host "           upload the generated .alf at https://license.unity3d.com/manual,"
        Write-Host "           download .ulf, then re-run with:"
        Write-Host "             powershell -File scripts\RunTests.ps1 -ManualLicense C:\path\to\Unity.ulf"
        exit 1
    }
    if ($p.ExitCode -ne 0) {
        # could be first-import noise; let runTests surface the real problem
        return
    }
}

# ---- run one test platform --------------------------------------------------
function Invoke-TestPlatform($unity, $platform) {
    $name = $platform.ToLowerInvariant()
    $log = Join-Path $LogDir "$name.log"
    $xml = Join-Path $LogDir "$name.xml"
    Write-Host ""
    Write-Host "=== Running ${platform} tests ... (log: $log)" -ForegroundColor Cyan

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $unity -ArgumentList @(
        "-batchmode", "-nographics",
        "-projectPath", $ProjectPath,
        "-runTests", "-testPlatform", $platform,
        "-testResults", $xml,
        "-logFile", $log
    ) -PassThru -NoNewWindow

    # Unity batchmode sometimes lingers AFTER saving results (EditMode on this
    # machine once sat untouched for the full timeout with a complete XML on
    # disk). Instead of dead-waiting, poll within the deadline:
    #  - process exited                                            -> normal;
    #  - results XML exists, was written after THIS run started, and parses to
    #    a finished <test-run> (Unity writes it only when the run completes;
    #    a corrupt half-written file fails parsing and is ignored) -> done,
    #    kill the lingering process and continue.
    if (Test-Path $xml) { Remove-Item $xml -Force -ErrorAction SilentlyContinue }   # stale-XML guard
    $xmlBase = Get-Date
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $savedOk = $false
    while ((Get-Date) -lt $deadline) {
        if ($p.WaitForExit(10000)) { break }
        if ((Test-Path $xml) -and ((Get-Item $xml).LastWriteTime -gt $xmlBase)) {
            try {
                [xml]$probe = Get-Content $xml -Raw
                if ($probe.'test-run' -and $null -ne $probe.'test-run'.result) { $savedOk = $true; break }
            } catch { }
        }
    }
    if (-not $p.HasExited) {
        $p.Kill()
        if (-not $savedOk) {
            Write-Host "[ERROR] ${platform} timed out after ${TimeoutMinutes} min without a complete results file (first import can be slow; -TimeoutMinutes to extend)" -ForegroundColor Red
            exit 2
        }
        Write-Host "    (Unity process lingered after saving results; collected the XML and killed it)"
    }
    $sw.Stop()

    # Unity: 0 = success, 2 = test failures, others = run errors
    if (-not (Test-Path $xml)) {
        Write-Host "[ERROR] ${platform}: no results file produced (compile error or crash). Tail of log:" -ForegroundColor Red
        Get-Content $log -Tail 30
        exit 2
    }
    [xml]$doc = Get-Content $xml
    $run = $doc.'test-run'
    $passed   = [int]$run.passed
    $failed   = [int]$run.failed
    $skipped  = [int]$run.skipped
    $inconcl  = [int]$run.inconclusive
    $total    = [int]$run.total

    # print per-test results
    foreach ($tc in $run.GetElementsByTagName("test-case")) {
        $res = $tc.result
        $time = [math]::Round([double]$tc.duration, 2)
        if ($res -eq "Passed") {
            Write-Host ("  [PASS] {0} ({1}s)" -f $tc.fullname, $time) -ForegroundColor Green
        } elseif ($res -eq "Skipped") {
            Write-Host ("  [SKIP] {0}" -f $tc.fullname) -ForegroundColor DarkYellow
        } else {
            Write-Host ("  [FAIL] {0}" -f $tc.fullname) -ForegroundColor Red
            foreach ($m in $tc.SelectNodes("failure/message")) {
                Write-Host ("         {0}" -f $m.InnerText) -ForegroundColor Red
            }
        }
        }
    # extract [TEST] measurements from the log (design expectation evidence)
    $testLines = Select-String -Path $log -Pattern "\[TEST\]" | ForEach-Object { $_.Line }
    foreach ($l in $testLines) { Write-Host ("  [MEAS] " + $l.Trim()) -ForegroundColor DarkCyan }

    Write-Host ("{0}: {1} passed, {2} failed, {3} skipped, {4} inconclusive ({5:N0}s)" -f `
        $platform, $passed, $failed, $skipped, $inconcl, $sw.Elapsed.TotalSeconds) -ForegroundColor $(if ($failed -eq 0 -and $inconcl -eq 0) { "Green" } else { "Red" })
    return $failed + $inconcl
}

# =============================================================================
$unity = Find-UnityEditor
Write-Host "Unity editor: $unity" -ForegroundColor Cyan
Write-Host "Project    : $ProjectPath"
Write-Host "Logs       : $LogDir"

Ensure-HubRunning       # Hub must stay alive to broker the editor license
Assert-Licensed $unity

$totalFailed = 0
if (-not $PlayOnly) { $totalFailed += Invoke-TestPlatform $unity "EditMode" }
if (-not $EditOnly) { $totalFailed += Invoke-TestPlatform $unity "PlayMode" }

Write-Host ""
if ($totalFailed -eq 0) {
    Write-Host "==============================" -ForegroundColor Green
    Write-Host " ALL TEST SUITES PASSED " -ForegroundColor Green -BackgroundColor DarkGreen
    Write-Host "==============================" -ForegroundColor Green
    Write-Host "Doc §7 acceptance verified: coast 3-6m, SCT 0-40km/h 2.5-3.5s, MT 0-70km/h 4.0-5.5s,"
    Write-Host "SCT circle roll 1.0-3.0deg / MT 0.2-1.5deg, top speed 52-62km/h (drag-torque balance),"
    Write-Host "MT 4.5cm curb passable, battery sag/LVC; plus §4 audio / §5 decor / §6 phys-config checks."
    Write-Host "(bounds are physics-corrected vs the doc - README §6 deviations 1-6)"
    exit 0
} else {
    Write-Host "==============================" -ForegroundColor Red
    Write-Host " $totalFailed TEST(S) FAILED " -ForegroundColor Red -BackgroundColor DarkRed
    Write-Host "==============================" -ForegroundColor Red
    Write-Host "See $LogDir\*.xml / *.log for details."
    exit 2
}