@echo off
setlocal
rem Compiles TopBar.cs with the C# compiler that ships with Windows (no install needed).

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe
  pause
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /out:TopBar.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll TopBar.cs
if errorlevel 1 (
  echo Build failed
  pause
  exit /b 1
)

echo.
echo Built TopBar.exe
echo Move it to a permanent folder first, then double-click it once.
echo It enables "Start with Windows" automatically on first launch.
pause
