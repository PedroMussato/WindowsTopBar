# TopBar

A tiny macOS-style top bar for Windows that always shows the **date, time and battery level**, so you never have to hover an auto-hidden taskbar to see them.

```
Monday, November 23, 2026  •  21:59  •  ⚡ 87%  •  🟢 Camera
```

## Features

- Docked at the top of **every monitor**, centered text, dark and minimal
- **Reserves screen space** like the macOS menu bar: maximized windows start below it
- Date, 12h/24h clock (configurable), battery percentage with a ⚡ charging indicator
- **Starts with Windows** automatically (can be turned off)
- Steps behind full-screen apps and videos, and comes back afterwards
- Handles monitors being plugged in or removed, and wake from sleep
- Tiny: a single C# file, no installer, no dependencies, negligible CPU and RAM
- **Camera status**: a green dot when any app is using the camera, red when it is not (can be hidden)
- On desktops without a battery, the battery part is simply left out

## How it works

TopBar registers itself as a Windows **AppBar** (the same mechanism the taskbar uses) through `SHAppBarMessage`. Windows then treats the strip at the top of each monitor as reserved, so the usable desktop area shrinks by the height of the bar.

The camera status reads the same per-app "last used start/stop" records that Windows keeps for its own camera privacy indicator (`HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam`). If an app has started using the camera and has not stopped, the dot turns green. No admin rights are needed. This is internal Windows bookkeeping rather than a documented API, so it could change in a future Windows update.

## Requirements

- Windows 10 or 11
- Nothing to install. The build uses the C# compiler that ships with Windows (.NET Framework 4.x).

## Build

1. Put `TopBar.cs` and `build.bat` in the same folder.
2. Double-click `build.bat`. It produces `TopBar.exe`.

Or compile by hand from a terminal:

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:TopBar.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll TopBar.cs
```

The source is written in C# 5 on purpose, so it compiles with the built-in compiler. It also builds fine with a newer .NET SDK if you prefer.

## Install and run

1. Move `TopBar.exe` to a permanent folder, for example `C:\Tools\TopBar`.
2. Double-click it once.

On first launch it enables **Start with Windows** (a per-user entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`). If you move the exe later, run it once from the new location to update that entry.

## Settings

Right-click the tray icon:

| Option | What it does |
| --- | --- |
| 24-hour clock / 12-hour clock | Switches the time format (`21:59` or `9:59 PM`) |
| Show time | Shows or hides the time, leaving only date and battery |
| Show camera status | Shows or hides the green/red camera dot |
| Start with Windows | Turns autostart on or off |
| Exit | Closes the bar and gives the reserved screen space back |

Settings are stored in `%APPDATA%\TopBar\settings.ini`.

## Uninstall

1. Right-click the tray icon and untick **Start with Windows**, then choose **Exit**.
2. Delete `TopBar.exe`.
3. Optionally delete the `%APPDATA%\TopBar` folder.

## Notes and limitations

- If your taskbar is docked at the **top** edge, it will compete with TopBar for that space. Keep the taskbar at the bottom or on a side.
- The bar is a fixed height based on the font size and scales with display scaling.
- Only one instance runs at a time.
- If `build.bat` says it cannot find `csc.exe`, check that `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\` exists on your machine.

## License

Add your preferred license here (for example MIT).
