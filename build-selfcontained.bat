@echo off
setlocal
cd /d "%~dp0"

if exist obj rmdir /s /q obj
if exist bin rmdir /s /q bin
if exist publish-selfcontained rmdir /s /q publish-selfcontained

echo Building self-contained Radmir Monitor Offline...
dotnet publish RadmirMonitor.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o publish-selfcontained
if errorlevel 1 goto fail

echo.
echo READY: %CD%\publish-selfcontained\RadmirMonitor.exe
echo This EXE includes the .NET 8 Desktop Runtime.
pause
exit /b 0

:fail
echo BUILD FAILED
pause
exit /b 1
