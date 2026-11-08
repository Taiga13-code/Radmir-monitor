@echo off
setlocal
cd /d "%~dp0"

if exist obj rmdir /s /q obj
if exist bin rmdir /s /q bin
if exist publish rmdir /s /q publish

echo Building Radmir Monitor Offline...
dotnet publish RadmirMonitor.Wpf.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o publish
if errorlevel 1 goto fail

if not exist publish\RadmirMonitor.exe goto fail
echo.
echo READY: %CD%\publish\RadmirMonitor.exe
echo This build requires the .NET 8 Desktop Runtime.
pause
exit /b 0

:fail
echo.
echo BUILD FAILED
pause
exit /b 1
