@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /optimize /target:winexe /out:LayoutFix.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /resource:data\en.txt,en.txt /resource:data\ru.txt,ru.txt /resource:CHANGELOG.md,changelog.txt /resource:fish.ico,fish.ico /win32icon:fish.ico Version.cs Program.cs Converter.cs CaseConverter.cs Detector.cs KeyboardWatcher.cs SpellService.cs
