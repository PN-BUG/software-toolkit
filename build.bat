@echo off
setlocal
if not "%~1"=="" goto custom
rem Default to the small framework-dependent package. Pass -SelfContained when the target PC has no .NET 8 Desktop Runtime.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Zip -OpenOutput
goto done
:custom
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
:done
set "buildExitCode=%errorlevel%"
if not "%buildExitCode%"=="0" echo Build failed. See the error above.
pause
exit /b %buildExitCode%
