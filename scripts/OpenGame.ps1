# =============================================================================
# RC Plaza - launch the game (graphics mode)
# Opens the Unity editor on this project and loads the game scene
# Assets/RCPlaza/Scenes/RCPlaza_Minimal.unity; press Play in the editor to play.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\OpenGame.ps1        # launch
#   powershell -ExecutionPolicy Bypass -File scripts\OpenGame.ps1 -Stop  # stop
#
# Notes:
#   - The editor is a GUI app: the script returns immediately after launching.
#   - Unity Hub must stay alive (it brokers the editor license); the script
#     starts it automatically if missing.
#   - First import takes 1-2 min; once the editor window shows up, press Play.
#   - -Stop kills the Unity editor only and keeps Unity Hub running.
# =============================================================================
param(
    [string]$EditorPath = "",
    [string]$ProjectPath = "",
    [switch]$Stop
)

$ErrorActionPreference = "Stop"
$UnityVersion = "2022.3.20f1"

# ---- project path (defaults to this script's parent directory) --------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($ProjectPath)) { $ProjectPath = Split-Path -Parent $ScriptDir }

# ---- locate the Unity editor ------------------------------------------------
function Find-UnityEditor {
    if ($EditorPath -ne "") {
        if (Test-Path $EditorPath) { return $EditorPath }
        Write-Host "[ERROR] EditorPath not found: $EditorPath" -ForegroundColor Red
        exit 1
    }
    $default = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
    if (Test-Path $default) { return $default }
    Write-Host "[ERROR] Unity $UnityVersion not found under Unity Hub installs." -ForegroundColor Red
    Write-Host "        Install it via Unity Hub, or pass -EditorPath <Unity.exe>."
    exit 1
}

# ---- keep Unity Hub alive: the Hub process brokers the editor license -------
function Ensure-HubRunning {
    if (Get-Process "Unity Hub" -ErrorAction SilentlyContinue) { return }
    try {
        # MSIX-packaged Hub: "<InstallLocation>\Unity Hub.exe" cannot be started
        # directly (file-not-found); the shell:AppsFolder alias of the installed
        # package works.
        Start-Process 'shell:AppsFolder\UnityTechnologies.UnityHub_2vrhnee42bhxm!UnityHub'
        Start-Sleep -Seconds 8
    } catch { }
}

$unity = Find-UnityEditor

# ---- stop --------------------------------------------------------------------
if ($Stop) {
    $editors = @(Get-Process "Unity" -ErrorAction SilentlyContinue)
    if ($editors.Count -gt 0) {
        $editors | Stop-Process -Force
        Write-Host ("Stopped Unity editor ({0} process(es)). Unity Hub kept alive (license broker)." -f $editors.Count)
    } else {
        Write-Host "No Unity editor process is running; nothing to stop."
    }
    exit 0
}

# ---- launch -------------------------------------------------------------------
if (Get-Process "Unity" -ErrorAction SilentlyContinue) {
    Write-Host "[NOTE] A Unity editor is already running:" -ForegroundColor Yellow
    Write-Host "       - if it has this project open, switch to its window;"
    Write-Host "       - to relaunch, stop it first: powershell -ExecutionPolicy Bypass -File scripts\OpenGame.ps1 -Stop"
    exit 0
}

Ensure-HubRunning

Write-Host "Unity editor: $unity" -ForegroundColor Cyan
Write-Host "Launching the editor on $ProjectPath with scene Assets/RCPlaza/Scenes/RCPlaza_Minimal.unity ..." -ForegroundColor Cyan
Write-Host "(first import takes 1-2 min; once the window opens, press Play. This command returns now.)"

Start-Process -FilePath $unity -ArgumentList @(
    "-projectPath", $ProjectPath,
    "-openScene", "Assets/RCPlaza/Scenes/RCPlaza_Minimal.unity"
)
Start-Sleep -Seconds 3
if (Get-Process "Unity" -ErrorAction SilentlyContinue) {
    Write-Host "OK - Unity editor started in the background; watch for its window." -ForegroundColor Green
    exit 0
}
Write-Host "[ERROR] Unity did not start within 3 s." -ForegroundColor Red
Write-Host "        Check that the editor path exists and the license is active (keep Unity Hub running)."
exit 1