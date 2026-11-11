@echo off
rem Built by VLOptimizer.Engine.Stub.csproj. Uses %~dp0 so paths containing spaces need no quoting
rem at the call site, and %~dp1/%~dp2 for the arguments.
setlocal
call "C:\Program\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
if errorlevel 1 ( echo vcvars64 failed & exit /b 2 )
set "SRC=%~dp0"
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0..\..\artifacts\private\engine-stub"
if not exist "%OUT%" mkdir "%OUT%"
rem Object file goes to a fixed name so no trailing-backslash path is ever quoted.
cl /nologo /LD /MT /O2 /W4 /EHsc "%SRC%EngineProbe.cpp" /Fe:"%OUT%\VL.EngineProbe.dll" /Fo:"%OUT%\EngineProbe.obj" /link /DLL /SUBSYSTEM:WINDOWS kernel32.lib user32.lib
exit /b %errorlevel%
