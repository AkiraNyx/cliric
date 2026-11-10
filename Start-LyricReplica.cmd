@echo off
setlocal
if not exist "%~dp0artifacts\current\LyricReplica.exe" (
  echo Build the app first: pwsh -NoProfile -File scripts\Build.ps1 -Publish
  exit /b 1
)
start "" "%~dp0artifacts\current\LyricReplica.exe" %*
