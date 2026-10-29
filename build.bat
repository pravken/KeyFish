@echo off
cd /d "%~dp0"
rem Word lists are embedded gzip-compressed: obj\<lang>.txt.gz
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; New-Item -ItemType Directory -Force obj | Out-Null; Get-ChildItem data\*.txt | ForEach-Object { $in = [IO.File]::OpenRead($_.FullName); $out = [IO.File]::Create('obj\' + $_.Name + '.gz'); $gz = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Optimal); $in.CopyTo($gz); $gz.Dispose(); $out.Dispose(); $in.Dispose() }" || exit /b 1
rem OUT can name another output file, e.g. to build while KeyFish is running
if "%OUT%"=="" set OUT=KeyFish.exe
set RES=
for %%f in (data\*.txt) do call set RES=%%RES%% /resource:obj\%%~nxf.gz,%%~nxf.gz
rem Interface translations: i18n\<lang>.txt
for %%f in (i18n\*.txt) do call set RES=%%RES%% /resource:i18n\%%~nxf,i18n-%%~nxf
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /optimize /target:winexe /out:%OUT% /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /lib:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF" /r:Accessibility.dll /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll %RES% /resource:CHANGELOG.md,changelog-en.txt /resource:CHANGELOG.ru.md,changelog-ru.txt /resource:fish.ico,fish.ico /resource:fish-ru.ico,fish-ru.ico /resource:fish-blink.ico,fish-blink.ico /resource:fish-ru-blink.ico,fish-ru-blink.ico /win32icon:fish.ico Version.cs Program.cs Tray.cs Hotkeys.cs Updates.cs Native.cs CrashLog.cs L.cs CaseConverter.cs Layouts.cs Ui.cs FishLogo.cs Detector.cs KeyboardWatcher.cs SpellService.cs Transliterator.cs SettingsForm.cs Updater.cs PasswordGuard.cs AboutForm.cs