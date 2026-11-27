# Compile HoldKeys.exe avec le compilateur C# inclus dans Windows (.NET Framework 4)
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wm = "$env:WINDIR\System32\WinMetadata"   # API Windows (OCR) fournies par le système
Set-Location $PSScriptRoot
if (-not (Test-Path HoldKeys.ico)) { powershell -NoProfile -ExecutionPolicy Bypass -File .\make-icon.ps1 }
& "$fw\csc.exe" /nologo /target:winexe /codepage:65001 /optimize+ /out:HoldKeys.exe `
    /win32icon:HoldKeys.ico /resource:HoldKeys.ico,HoldKeys.ico `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Data.dll /r:System.Security.dll `
    /r:System.Core.dll /r:Microsoft.CSharp.dll /r:System.Web.Extensions.dll `
    /r:"$fw\System.Runtime.dll" /r:"$wm\Windows.Foundation.winmd" /r:"$wm\Windows.Media.winmd" `
    /r:"$wm\Windows.Graphics.winmd" /r:"$wm\Windows.Storage.winmd" /r:"$wm\Windows.Globalization.winmd" `
    HoldKeys.cs Settings.cs Search.cs Launcher.cs Providers.cs Extras.cs Paste.cs
