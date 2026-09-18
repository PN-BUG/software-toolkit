@echo off
setlocal
rem Keep cmd.exe and PowerShell on the same code page so Chinese build output is not garbled.
chcp 65001 >nul
if not "%~1"=="" goto custom
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -OpenOutput
goto done
:custom
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
:done
set "buildExitCode=%errorlevel%"
if not "%buildExitCode%"=="0" (echo Build failed. See the error above.) else (echo Packaging complete. Files are in: %~dp0release)
pause
exit /b %buildExitCode%
