# Runs the unpackaged build on the desktop and captures each step in both languages.
# Used by CI to check the interface on a real Windows machine.
# Usage: packaging/screenshots.ps1 <app-directory> <output-directory>
param([string]$AppDir, [string]$OutDir)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
try { Set-DisplayResolution -Width 1920 -Height 1080 -Force | Out-Null } catch { Write-Host "resolution unchanged: $_" }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$photos = Join-Path $env:RUNNER_TEMP 'cdu-photos'
New-Item -ItemType Directory -Force $photos | Out-Null
foreach ($i in 1..5) {
  $bitmap = New-Object System.Drawing.Bitmap 1500, 1000
  $g = [System.Drawing.Graphics]::FromImage($bitmap)
  $g.Clear([System.Drawing.Color]::FromArgb(0x1f, 0x29, 0x78))
  $font = New-Object System.Drawing.Font 'Arial', 220, ([System.Drawing.FontStyle]::Bold)
  $g.DrawString([string]($i * 7), $font, [System.Drawing.Brushes]::Gold, 520, 300)
  $g.Dispose()
  $bitmap.Save((Join-Path $photos "SAMPLE$i.JPG"), [System.Drawing.Imaging.ImageFormat]::Jpeg)
  $bitmap.Dispose()
}
Copy-Item (Join-Path $PSScriptRoot 'sample-match.json') (Join-Path $photos 'commons-yukleme.json') -Force
$exe = Join-Path $AppDir 'CommonsDiversifiedUploader.exe'
function Shot([string]$name, [double]$seconds, [hashtable]$vars) {
  foreach ($k in 'CDU_LANG', 'CDU_FOLDER', 'CDU_STEP', 'CDU_MAXIMIZE') { Remove-Item "Env:$k" -ErrorAction SilentlyContinue }
  foreach ($k in $vars.Keys) { Set-Item "Env:$k" $vars[$k] }
  $process = Start-Process $exe -PassThru
  Start-Sleep -Milliseconds ([int]($seconds * 1000))
  $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
  $g = [System.Drawing.Graphics]::FromImage($bitmap)
  $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
  $g.Dispose()
  $bitmap.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $bitmap.Dispose()
  if ($process.HasExited) { Write-Host "${name}: process exited early with code $($process.ExitCode)"; $script:failed = $true }
  else { Stop-Process -Id $process.Id -Force }
  Start-Sleep -Seconds 1
}
$script:failed = $false
Shot 'splash-tr' 2.6 @{ CDU_LANG = 'tr' }
Shot 'welcome-en' 7 @{ CDU_LANG = 'en' }
foreach ($lang in 'tr', 'en') {
  foreach ($step in 'match', 'roster', 'photos', 'upload') {
    Shot "$step-$lang" 9 @{ CDU_LANG = $lang; CDU_FOLDER = $photos; CDU_STEP = $step; CDU_MAXIMIZE = '1' }
  }
}
Get-ChildItem $OutDir
Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 2; StartTime = (Get-Date).AddMinutes(-10) } -ErrorAction SilentlyContinue |
  Where-Object { $_.Message -match 'CommonsDiversifiedUploader' } | Select-Object -First 3 | Format-List TimeCreated, Message
if ($script:failed) { exit 1 }
