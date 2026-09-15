@echo off
setlocal
if not "%~1"=="" goto custom
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -SelfContained -Zip -OpenOutput
goto done
:custom
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
:done
set "buildExitCode=%errorlevel%"
if not "%buildExitCode%"=="0" echo Build failed. See the error above.
pause
exit /b %buildExitCode%
