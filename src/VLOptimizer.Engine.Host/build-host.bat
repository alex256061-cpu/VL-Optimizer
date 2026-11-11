@echo off
rem Builds VLEngineHost.exe (console) beside the probe output. Uses %~dp0 so the paths containing
rem spaces never need quoting at the call site.
setlocal
call "C:\Program\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
if errorlevel 1 ( echo vcvars64 failed & exit /b 2 )
set "SRC=%~dp0"
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0..\..\artifacts\private\engine-host"
if not exist "%OUT%" mkdir "%OUT%"
cl /nologo /MT /O2 /W4 /EHsc "%SRC%EngineHost.cpp" /Fe:"%OUT%\VLEngineHost.exe" /Fo:"%OUT%\EngineHost.obj" /link /SUBSYSTEM:CONSOLE kernel32.lib user32.lib d3d11.lib dxgi.lib
exit /b %errorlevel%
