# Désinstalle HoldKeys (arrêt, démarrage auto, raccourci, fichiers)
Get-Process HoldKeys -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'HoldKeys' -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'HoldKeys.lnk') -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\HoldKeys') -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "HoldKeys désinstallé."
