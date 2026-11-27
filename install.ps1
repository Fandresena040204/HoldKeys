# Installe HoldKeys pour l'utilisateur courant (pas besoin d'admin) :
#  - copie dans %LOCALAPPDATA%\Programs\HoldKeys
#  - raccourci dans le menu Démarrer
#  - lancement automatique à l'ouverture de session
#  - démarre l'application
$ErrorActionPreference = 'Stop'
$src  = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\HoldKeys'
$exe  = Join-Path $dest 'HoldKeys.exe'

& (Join-Path $src 'build.ps1')
if (-not (Test-Path (Join-Path $src 'HoldKeys.exe'))) { throw 'Échec de la compilation' }

$old = Get-Process HoldKeys -ErrorAction SilentlyContinue
if ($old) { $old | Stop-Process -Force; $old | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 300 }
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $src 'HoldKeys.exe') $dest -Force
Copy-Item (Join-Path $src 'uninstall.ps1') $dest -Force
# Config initiale seulement : on ne remplace pas les réglages faits dans la fenêtre Paramètres
if (-not (Test-Path (Join-Path $dest 'HoldKeys.txt'))) { Copy-Item (Join-Path $src 'HoldKeys.txt') $dest }

# Menu Démarrer
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'HoldKeys.lnk'
$sh = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
$sh.TargetPath = $exe
$sh.WorkingDirectory = $dest
$sh.Description = 'Caractères spéciaux : maintenir une lettre + Espace'
$sh.IconLocation = "$exe,0"
$sh.Save()

# Démarrage automatique
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'HoldKeys' -Value "`"$exe`" --background"

Start-Process $exe
Write-Host "HoldKeys installé dans $dest et lancé."
