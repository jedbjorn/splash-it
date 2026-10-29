<#
  test-detection.ps1 - prove the MSI's Revit detection with real silent installs
  (spec #1 AC10, CI part). Windows only, and ADMIN only: it writes fake Revit
  uninstall entries under HKLM and fake Revit folders under Program Files.
  Meant for a throwaway CI runner (the MSI job of .github/workflows/_build.yml);
  it cleans up after itself, but do not run it on a machine with Revit.

    powershell -File installer/test-detection.ps1 -Msi <SplashIt-<v>.msi> -OutDir <folder> [-Expected <folder>]

  New in Splash-It, with no gantt-it counterpart: gantt-it (commit aa83f35)
  checks the same detection only statically, from the MSI tables
  (inspect-msi.ps1). This script installs
  the MSI for real, the way rst-c's chained setup will (msiexec /qn), against
  fake Revit installs:

    - an uninstall entry HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\
      Uninstall\{7346B4A0-<yy>00-0510-0000-705C0D862004} (64-bit view) whose
      InstallLocation points at a temp folder, with or without a dummy
      Revit.exe in it;
    - a dummy C:\Program Files\Autodesk\Revit <year>\Revit.exe, the default
      folder, with no uninstall entry.

  After each msiexec run it checks exactly which
  %AppData%\Autodesk\Revit\Addins\<year>\SplashIt* exist, and with -Expected
  (a folder holding <year>\ trees, the unzipped SplashIt-<v>-Revit<year>.zip
  files) that each installed year holds exactly the expected files. Splash-It
  is uninstalled and the fakes removed between cases.

  Cases:
    1   no Revit at all: the first install is blocked by the launch condition
        (nonzero exit, the message in the log), nothing installed;
    2   2026 in a custom folder, found through the registry -> only 2026;
    3   a 2025 entry whose folder has no Revit.exe, and 2027 in the default
        folder only -> only 2027;
    4   all three (2025 and 2026 through the registry, 2027 in the default
        folder) -> all three;
    5   install with 2026 only, then add 2025 and repair -> 2025 and 2026;
    5b  remove 2026, repair again -> only 2025 (a repair drops a year whose
        Revit is gone);
    6   remove 2025 too (no Revit left) and uninstall -> not blocked, every
        Splash-It folder gone; Addins\2025\ (which existed, empty, before
        case 5) and Addins\2026\ kept, another add-in's file in it kept, and
        the pre-seeded %AppData%\SplashIt\settings.json kept unchanged.
        (An Addins\<year>\ that the MSI itself created, empty, is removed with
        it by Windows Installer, leaving the folder tree as it was.)

  Repair is what the setup UI's Repair button does (WixUI_Minimal,
  VerifyReadyDlg): REINSTALL=ALL REINSTALLMODE=ecmus, here with /qn.

  Writes <OutDir>\results.md (a table, one row per case), <OutDir>\results.txt
  (the full transcript) and <OutDir>\logs\*.log (every msiexec verbose log).
  Exits 1 if any check failed.
#>
param(
  [Parameter(Mandatory = $true)] [string] $Msi,
  [Parameter(Mandatory = $true)] [string] $OutDir,
  [string] $Expected
)

$ErrorActionPreference = 'Stop'
$UpgradeCode = '{080648F4-EEC7-4DB6-A793-683A1C008BFC}'
$Years = '2025', '2026', '2027'
$LaunchMessage = 'Splash-It needs Revit 2025, 2026 or 2027, and none was found on this computer.'

$msiPath = (Resolve-Path $Msi).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$logDir = Join-Path $OutDir 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$transcript = Join-Path $OutDir 'results.txt'
Set-Content -Path $transcript -Value "Splash-It detection test: $msiPath" -Encoding UTF8

function Say([string] $text) {
  Write-Host $text
  Add-Content -Path $transcript -Value $text -Encoding UTF8
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw 'test-detection.ps1 must run as an administrator (it writes fake Revit entries under HKLM).'
}
if (-not [Environment]::Is64BitProcess) { throw 'test-detection.ps1 must run in a 64-bit PowerShell.' }

$addins = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
$settingsDir = Join-Path $env:APPDATA 'SplashIt'
$settingsFile = Join-Path $settingsDir 'settings.json'
$programFiles = [Environment]::GetFolderPath('ProgramFiles')   # 64-bit process: C:\Program Files
$fakeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('splashit-fake-revit-' + [guid]::NewGuid().ToString('N'))
$hklm64 = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
$installer = New-Object -ComObject WindowsInstaller.Installer

function UninstallKey([string] $year) {
  'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7346B4A0-' + $year.Substring(2) + '00-0510-0000-705C0D862004}'
}
function DefaultFolder([string] $year) { Join-Path $programFiles "Autodesk\Revit $year" }

# --- fake Revit installs ----------------------------------------------------

# A Revit <year> uninstall entry (64-bit view) whose InstallLocation is a temp
# folder, with or without a dummy Revit.exe in it. Real entries end the path
# with a backslash; -NoTrailingSlash checks the search copes without one.
function Add-RegistryRevit([string] $year, [switch] $WithoutExe, [switch] $NoTrailingSlash) {
  $dir = Join-Path $fakeRoot "Custom Revit $year"
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  if (-not $WithoutExe) { Set-Content -Path (Join-Path $dir 'Revit.exe') -Value 'dummy Revit.exe (Splash-It detection test)' }
  $location = if ($NoTrailingSlash) { $dir } else { "$dir\" }
  $key = $hklm64.CreateSubKey((UninstallKey $year))
  $key.SetValue('DisplayName', "Revit $year (Splash-It detection test fake)")
  $key.SetValue('InstallLocation', $location)
  $key.Close()
  $exe = if ($WithoutExe) { 'no Revit.exe' } else { 'with Revit.exe' }
  Say "    fake: Revit $year uninstall entry -> InstallLocation '$location' ($exe)"
}
function Add-DefaultFolderRevit([string] $year) {
  $dir = DefaultFolder $year
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  Set-Content -Path (Join-Path $dir 'Revit.exe') -Value 'dummy Revit.exe (Splash-It detection test)'
  Say "    fake: $dir\Revit.exe (default folder, no uninstall entry)"
}
function Remove-FakeRevit([string] $year) {
  $hklm64.DeleteSubKeyTree((UninstallKey $year), $false)
  Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $fakeRoot "Custom Revit $year")
  Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (DefaultFolder $year)
}
function Remove-AllFakes {
  foreach ($y in $Years) { Remove-FakeRevit $y }
  $autodesk = Join-Path $programFiles 'Autodesk'
  if ((Test-Path $autodesk) -and -not (Get-ChildItem -Force $autodesk)) { Remove-Item -Force $autodesk }
}

# --- msiexec ------------------------------------------------------------------

$script:logIndex = 0
function Invoke-Msiexec([string] $name, [string[]] $arguments) {
  $script:logIndex++
  $log = Join-Path $logDir ('{0:D2}-{1}.log' -f $script:logIndex, $name)
  $all = $arguments + @('/qn', '/l*v', "`"$log`"")
  for ($attempt = 1; $attempt -le 5; $attempt++) {
    $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList $all -Wait -PassThru
    # 1618: another installation is in progress (the msiserver is still busy
    # with the previous run). Wait and retry.
    if ($p.ExitCode -ne 1618) { break }
    Start-Sleep -Seconds 5
  }
  Say ("    msiexec {0} -> exit {1}  (log {2})" -f ($arguments -join ' '), $p.ExitCode, (Split-Path -Leaf $log))
  return @{ ExitCode = $p.ExitCode; Log = $log }
}
function Install-Msi([string] $name) { Invoke-Msiexec $name @('/i', "`"$msiPath`"") }
function Repair-Msi([string] $name) { Invoke-Msiexec $name @('/i', "`"$msiPath`"", 'REINSTALL=ALL', 'REINSTALLMODE=ecmus') }
function Uninstall-Msi([string] $name) { Invoke-Msiexec $name @('/x', "`"$msiPath`"") }

# --- state checks -----------------------------------------------------------

# The installed products of Splash-It's UpgradeCode (Windows Installer's
# RelatedProducts; per-user installs of this user included). Windows
# Installer hands back no list at all when there are none.
function Get-InstalledProducts {
  $codes = @()
  $list = $installer.RelatedProducts($UpgradeCode)
  if ($null -ne $list) { foreach ($c in $list) { $codes += [string]$c } }
  return , $codes
}
# AssignmentType 0 = installed per-user (1 = per-machine).
function Test-PerUser {
  $problems = @()
  foreach ($c in (Get-InstalledProducts)) {
    $t = [string]$installer.ProductInfo($c, 'AssignmentType')
    Say "    installed product $c, AssignmentType $t"
    if ($t -ne '0') { $problems += "product $c has AssignmentType '$t', expected 0 (per-user)" }
  }
  return , $problems
}

# Years whose Addins\<year>\ holds anything of Splash-It's.
function Get-SplashItYears {
  $found = @()
  foreach ($y in $Years) {
    $dir = Join-Path $addins $y
    if ((Test-Path $dir) -and @(Get-ChildItem -Force -Path $dir -Filter 'SplashIt*').Count -gt 0) { $found += $y }
  }
  return , $found
}

function Get-RelativeFiles([string] $root) {
  $full = (Resolve-Path $root).Path.TrimEnd('\') + '\'
  return , @(Get-ChildItem -Recurse -File -Force -Path $root | ForEach-Object { $_.FullName.Substring($full.Length) } | Sort-Object)
}

# Checks the computer against the years expected to hold Splash-It. Returns
# a list of problems (empty = pass).
function Test-State([string[]] $wantYears) {
  $problems = @()
  $have = Get-SplashItYears
  Say ("    Addins\<year>\SplashIt* present for: {0}" -f $(if ($have.Count) { $have -join ', ' } else { '(none)' }))
  foreach ($y in $Years) {
    $dir = Join-Path $addins $y
    $want = $wantYears -contains $y
    $addin = Test-Path (Join-Path $dir 'SplashIt.addin')
    $folder = Test-Path (Join-Path $dir 'SplashIt')
    if ($want) {
      if (-not $addin) { $problems += "Addins\$y\SplashIt.addin is missing" }
      if (-not $folder) { $problems += "Addins\$y\SplashIt\ is missing" }
      if ($addin -and $folder) {
        $inDir = Get-RelativeFiles $dir
        $files = @($inDir | Where-Object { $_ -like 'SplashIt*' })
        Say ("      {0}: {1}" -f $y, ($files -join ', '))
        if ($Expected) {
          $wanted = Get-RelativeFiles (Join-Path $Expected $y)
          $diff = Compare-Object -ReferenceObject $wanted -DifferenceObject $files
          if ($diff) { $problems += "Addins\$y does not hold exactly the expected files: $(($diff | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join '; ')" }
        }
      }
    } elseif ($have -contains $y) {
      $problems += "Addins\$y holds Splash-It but Revit $y was not installed"
    }
  }
  return , $problems
}

# --- run ----------------------------------------------------------------------

$results = @()
function Record([string] $id, [string] $setup, [string] $action, [string] $expect, [string[]] $problems, [string] $observed) {
  $pass = $problems.Count -eq 0
  foreach ($p in $problems) { Say "    FAIL: $p"; Write-Host "::error::case ${id}: $p" }
  Say ("  => case {0}: {1}" -f $id, $(if ($pass) { 'PASS' } else { 'FAIL' }))
  $script:results += [pscustomobject]@{
    Id = $id; Setup = $setup; Action = $action; Expected = $expect; Observed = $observed
    Result = $(if ($pass) { 'PASS' } else { 'FAIL: ' + ($problems -join '; ') })
  }
}
function Observed([hashtable] $run) {
  $y = Get-SplashItYears
  "exit $($run.ExitCode); Splash-It in: $(if ($y.Count) { $y -join ', ' } else { 'none' })"
}

# Preconditions: a clean runner. No real Revit, no earlier Splash-It.
Say "Runner: $env:COMPUTERNAME, user $env:USERNAME, AppData $env:APPDATA, Program Files $programFiles"
foreach ($y in $Years) {
  if ($hklm64.OpenSubKey((UninstallKey $y))) { throw "Revit $y uninstall entry already exists; not a clean runner." }
  if (Test-Path (Join-Path (DefaultFolder $y) 'Revit.exe')) { throw "Revit $y is installed in the default folder; not a clean runner." }
}
if ((Get-InstalledProducts).Count) { throw 'Splash-It is already installed; not a clean runner.' }
if ((Get-SplashItYears).Count) { throw "Splash-It files already exist under $addins; not a clean runner." }
Say "Addins\ before the first case: $(if (Test-Path $addins) { (@(Get-ChildItem -Force $addins | ForEach-Object { $_.Name }) -join ', ') + ' (exists)' } else { 'does not exist' })"

# A settings file written by the add-in, which the MSI must never touch.
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
Set-Content -Path $settingsFile -Encoding UTF8 -Value '{ "schemaVersion": 1, "enabled": false, "projectColors": ["#123456"], "familyColors": ["#654321"], "projectFillOpacity": 40, "familyBarWidth": 6, "familyBarOpacity": 30 }'
$settingsHash = (Get-FileHash $settingsFile).Hash
Say "Pre-seeded $settingsFile (sha256 $settingsHash)"

try {
  # Case 1 ---------------------------------------------------------------
  Say ''; Say 'Case 1: no Revit at all -> first install blocked'
  $run = Install-Msi 'case1-install'
  $p = @()
  if ($run.ExitCode -eq 0) { $p += 'install succeeded; the launch condition should have blocked it' }
  # msiexec writes its log as UTF-16 (with a BOM) or ANSI; ReadAllText reads either.
  if (-not ([System.IO.File]::ReadAllText($run.Log).Contains($LaunchMessage))) { $p += 'the launch condition message is not in the log' }
  $p += Test-State @()
  if ((Get-InstalledProducts).Count) { $p += 'Splash-It is registered as installed' }
  Record '1' 'no Revit' 'install' 'blocked (nonzero exit, launch-condition message), no folders' $p (Observed $run)
  Remove-AllFakes

  # Case 2 ---------------------------------------------------------------
  Say ''; Say 'Case 2: Revit 2026 in a custom folder, found through the registry -> only 2026'
  Add-RegistryRevit '2026'
  $run = Install-Msi 'case2-install'
  $p = @()
  if ($run.ExitCode -ne 0) { $p += "install exit $($run.ExitCode)" }
  $p += Test-State @('2026')
  if ((Get-InstalledProducts).Count -ne 1) { $p += "expected one installed Splash-It product, found $((Get-InstalledProducts).Count)" }
  $p += Test-PerUser
  $obs = Observed $run
  $un = Uninstall-Msi 'case2-uninstall'
  if ($un.ExitCode -ne 0) { $p += "uninstall exit $($un.ExitCode)" }
  $p += Test-State @()
  Record '2' '2026 registry entry, custom folder with Revit.exe' 'install' 'only 2026' $p $obs
  Remove-AllFakes

  # Case 3 ---------------------------------------------------------------
  Say ''; Say 'Case 3: a 2025 entry without Revit.exe, and 2027 in the default folder only -> only 2027'
  Add-RegistryRevit '2025' -WithoutExe
  Add-DefaultFolderRevit '2027'
  $run = Install-Msi 'case3-install'
  $p = @()
  if ($run.ExitCode -ne 0) { $p += "install exit $($run.ExitCode)" }
  $p += Test-State @('2027')
  $obs = Observed $run
  $un = Uninstall-Msi 'case3-uninstall'
  if ($un.ExitCode -ne 0) { $p += "uninstall exit $($un.ExitCode)" }
  $p += Test-State @()
  # Informational: Addins\2027\ did not exist before this install, so the MSI
  # created it; this shows whether Windows Installer removed it again, empty.
  Say "    (info) Addins\2027\ after the uninstall: $(if (Test-Path (Join-Path $addins '2027')) { 'present' } else { 'absent' })"
  Record '3' '2025 registry entry without Revit.exe; 2027 default folder only' 'install' 'only 2027' $p $obs
  Remove-AllFakes

  # Case 4 ---------------------------------------------------------------
  Say ''; Say 'Case 4: all three -> all three'
  Add-RegistryRevit '2025' -NoTrailingSlash
  Add-RegistryRevit '2026'
  Add-DefaultFolderRevit '2027'
  $run = Install-Msi 'case4-install'
  $p = @()
  if ($run.ExitCode -ne 0) { $p += "install exit $($run.ExitCode)" }
  $p += Test-State @('2025', '2026', '2027')
  if ((Get-InstalledProducts).Count -ne 1) { $p += "expected one installed Splash-It product, found $((Get-InstalledProducts).Count)" }
  $obs = Observed $run
  $un = Uninstall-Msi 'case4-uninstall'
  if ($un.ExitCode -ne 0) { $p += "uninstall exit $($un.ExitCode)" }
  $p += Test-State @()
  Record '4' '2025 + 2026 registry entries (custom folders), 2027 default folder' 'install' '2025, 2026 and 2027' $p $obs
  Remove-AllFakes

  # Case 5 ---------------------------------------------------------------
  Say ''; Say 'Case 5: install with 2026 only, then add 2025 and repair -> 2025 and 2026'
  # Revit's own folders as Revit leaves them: Addins\2025\ empty, and
  # Addins\2026\ holding another add-in's manifest. The uninstall in case 6
  # must keep both folders, and the manifest.
  foreach ($y in '2025', '2026') {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $addins $y)
    New-Item -ItemType Directory -Force -Path (Join-Path $addins $y) | Out-Null
  }
  Set-Content -Path (Join-Path $addins '2026\Other.addin') -Value '<RevitAddIns />'
  Say '    Revit folders: Addins\2025\ (empty) and Addins\2026\Other.addin exist before the install'
  Add-RegistryRevit '2026'
  $run = Install-Msi 'case5-install-2026'
  $p = @()
  if ($run.ExitCode -ne 0) { $p += "install exit $($run.ExitCode)" }
  $first = Test-State @('2026')
  foreach ($x in $first) { $p += "before repair: $x" }
  Add-RegistryRevit '2025'
  $run = Repair-Msi 'case5-repair-add-2025'
  if ($run.ExitCode -ne 0) { $p += "repair exit $($run.ExitCode)" }
  $p += Test-State @('2025', '2026')
  if ((Get-InstalledProducts).Count -ne 1) { $p += "expected one installed Splash-It product, found $((Get-InstalledProducts).Count)" }
  Record '5' 'install with 2026 only; then add a 2025 registry entry' 'repair (REINSTALL=ALL REINSTALLMODE=ecmus)' '2025 and 2026' $p (Observed $run)

  # Case 5b --------------------------------------------------------------
  Say ''; Say 'Case 5b: remove Revit 2026, repair -> only 2025'
  Remove-FakeRevit '2026'
  Say '    fake: Revit 2026 removed'
  $run = Repair-Msi 'case5b-repair-drop-2026'
  $p = @()
  if ($run.ExitCode -ne 0) { $p += "repair exit $($run.ExitCode)" }
  $p += Test-State @('2025')
  Record '5b' 'after case 5, remove the 2026 entry and folder' 'repair' 'only 2025' $p (Observed $run)

  # Case 6 ---------------------------------------------------------------
  Say ''; Say 'Case 6: remove Revit 2025 too (no Revit left), uninstall -> everything of Splash-It gone, the rest kept'
  Remove-FakeRevit '2025'
  Say '    fake: Revit 2025 removed (no supported Revit left)'
  $run = Uninstall-Msi 'case6-uninstall'
  $p = @()
  if ($run.ExitCode -ne 0) { $p += "uninstall exit $($run.ExitCode) (an uninstall must never be blocked)" }
  $p += Test-State @()
  foreach ($y in '2025', '2026') {
    if (-not (Test-Path (Join-Path $addins $y))) { $p += "Addins\$y\ was removed (Revit owns it)" }
  }
  if (-not (Test-Path (Join-Path $addins '2026\Other.addin'))) { $p += 'Addins\2026\Other.addin (another add-in) was removed' }
  if (-not (Test-Path $settingsFile)) { $p += "$settingsFile was removed" }
  elseif ((Get-FileHash $settingsFile).Hash -ne $settingsHash) { $p += "$settingsFile was changed" }
  if ((Get-InstalledProducts).Count) { $p += 'Splash-It is still registered as installed' }
  $kept = @(Get-ChildItem -Force $addins | ForEach-Object { $_.Name }) -join ', '
  Say "    Addins\ now holds: $kept; settings.json present: $(Test-Path $settingsFile)"
  Record '6' 'after 5b, remove 2025 too (no Revit); Addins\2025\ existed empty and Addins\2026\ held Other.addin before case 5; settings.json pre-seeded' 'uninstall' 'not blocked; no SplashIt* left; Addins\2025\ and Addins\2026\ kept, Other.addin and settings.json kept' $p "$(Observed $run); Addins\ holds: $kept; settings.json unchanged: $((Test-Path $settingsFile) -and (Get-FileHash $settingsFile).Hash -eq $settingsHash)"
}
finally {
  # Leave the runner as it was: no fakes, no Splash-It.
  Remove-AllFakes
  if ((Get-InstalledProducts).Count) { Uninstall-Msi 'cleanup-uninstall' | Out-Null }
  Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $fakeRoot
  Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $addins '2026\Other.addin')
  Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $settingsDir
}

# --- report -------------------------------------------------------------------

$md = @(
  '# Splash-It detection test (AC10, CI part)',
  '',
  "MSI: ``$(Split-Path -Leaf $msiPath)``. Runner: $env:COMPUTERNAME ($env:USERNAME). Repair = ``REINSTALL=ALL REINSTALLMODE=ecmus`` (the setup UI's Repair button), every run ``/qn``.",
  '',
  '| Case | Setup | Action | Expected | Observed | Result |',
  '|---|---|---|---|---|---|'
)
foreach ($r in $results) {
  $md += "| $($r.Id) | $($r.Setup) | $($r.Action) | $($r.Expected) | $($r.Observed) | $($r.Result) |"
}
Set-Content -Path (Join-Path $OutDir 'results.md') -Value $md -Encoding UTF8
Say ''
$md | ForEach-Object { Say $_ }

$failed = @($results | Where-Object { $_.Result -ne 'PASS' })
if ($failed.Count -or $results.Count -ne 7) {
  Write-Host "::error::$($failed.Count) of $($results.Count) detection cases failed (7 expected)"
  exit 1
}
Write-Host 'All detection cases passed.'
