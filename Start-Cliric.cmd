@echo off
setlocal
set "PATH=%~dp0tools\node;%PATH%"
if not exist "%~dp0artifacts\cliric\Cliric.exe" (
  echo Build the app first: pwsh -NoProfile -File scripts\Build.ps1 -Publish
  exit /b 1
)
start "" /D "%~dp0artifacts\cliric" "%~dp0artifacts\cliric\Cliric.exe" %*
