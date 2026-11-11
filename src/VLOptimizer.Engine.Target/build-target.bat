@echo off
setlocal
call "C:\Program\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
if errorlevel 1 ( echo vcvars64 failed & exit /b 2 )
set "SRC=%~dp0"
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0..\..\artifacts\private\engine-target"
if not exist "%OUT%" mkdir "%OUT%"
pushd "%OUT%"
cl /nologo /MT /O2 /W4 /EHsc "%SRC%RenderTarget.cpp" /Fe:"%OUT%\VLRenderTarget.exe" /link /SUBSYSTEM:CONSOLE kernel32.lib user32.lib d3d11.lib dxgi.lib d3dcompiler.lib
set "RC=%errorlevel%"
popd
exit /b %RC%
