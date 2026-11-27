# Compile HoldKeys.exe avec le compilateur C# inclus dans Windows (.NET Framework 4)
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
Set-Location $PSScriptRoot
if (-not (Test-Path HoldKeys.ico)) { powershell -NoProfile -ExecutionPolicy Bypass -File .\make-icon.ps1 }
& $csc /nologo /target:winexe /codepage:65001 /optimize+ /out:HoldKeys.exe `
    /win32icon:HoldKeys.ico /resource:HoldKeys.ico,HoldKeys.ico `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Data.dll /r:System.Security.dll `
    /r:System.Core.dll /r:Microsoft.CSharp.dll `
    HoldKeys.cs Settings.cs Search.cs Launcher.cs Providers.cs Extras.cs
