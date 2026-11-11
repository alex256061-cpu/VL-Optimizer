@echo off
rem Builds VLCompositor.exe. Uses %~dp0 so the paths containing spaces never need quoting at the
rem call site. Shaders are compiled at runtime with D3DCompile, so d3dcompiler_47.dll (present on
rem every Windows 10+) is the only shader dependency; nothing extra needs shipping.
setlocal
call "C:\Program\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
if errorlevel 1 ( echo vcvars64 failed & exit /b 2 )
set "SRC=%~dp0"
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0..\..\artifacts\private\engine-compositor"
if not exist "%OUT%" mkdir "%OUT%"
rem Compile from the output folder so cl places per-source .obj files there without a /Fo path
rem (a single /Fo path is rejected for multiple sources).
pushd "%OUT%"
cl /nologo /MT /O2 /W4 /EHsc "%SRC%CompositorHost.cpp" "%SRC%Compositor.cpp" /Fe:"%OUT%\VLCompositor.exe" /link /SUBSYSTEM:CONSOLE kernel32.lib user32.lib gdi32.lib d3d11.lib dxgi.lib d3dcompiler.lib
set "RC=%errorlevel%"
popd
exit /b %RC%
