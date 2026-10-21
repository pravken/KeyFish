@echo off
rem Runs the typing tests and writes the results to tests\bin\results.txt.
rem Don't touch the keyboard or mouse while they run (about 3 minutes).
cd /d "%~dp0"
if not exist bin mkdir bin
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-tests.ps1" > "%~dp0bin\results.txt" 2>&1
