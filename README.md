# clicky

Press a hotkey (default **Ctrl+Alt+Shift+M**) and every monitor gets a labelled grid. Type a cell's two characters, then a two-digit number (`00`–`15` by default) to pick a spot inside that cell, and clicky moves the mouse there and left-clicks.

It runs on **Windows** and **KDE Plasma 6 (Wayland)** and is written in .NET 10 with Avalonia.

## How labels work

- The first character picks the row and the second picks the column.
- Labels are unique across monitors. By default the 36 row characters (`a`–`z`, `0`–`9`) are split between monitors, ordered left to right and then top to bottom, and every monitor keeps all 36 columns. The `columns`, `rows` and `subGridSize` settings change this (see [Config](#config)).

| Monitors | Rows on each monitor |
|---|---|
| 1 | `a`–`9`: a 36×36 grid |
| 2 | `a`–`r`, then `s`–`9`: 18×36 each |
| 3 | `a`–`l`, `m`–`x`, `y`–`9`: 12×36 each |

- After you type the first character, its row lights up and everything else dims.
- After the second character, the chosen cell is split into a 4×4 grid (or `subGridSize`) numbered `00`–`15` in reading order (`00` top-left, `15` bottom-right). Type the two digits to click the centre of that square, or press `Enter`/`Space` to click the centre of the whole cell. If the cell is too small for readable numbers, it gets lines only and a magnified numbered key appears beside it.
- `Backspace` undoes the last character.
- `Esc` or a mouse click cancels.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). On Arch/CachyOS, install it with `sudo pacman -S dotnet-sdk`.

```sh
dotnet test                                   # unit + headless render tests
dotnet run --project src/Clicky               # run the daemon from source

# single-file, self-contained builds (both can be built from Linux)
dotnet publish src/Clicky -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/linux
dotnet publish src/Clicky -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/windows
```

## Usage

```sh
clicky             # run the background daemon
clicky show        # open the grid in the running daemon
clicky install     # start on login (HKCU Run key / ~/.config/autostart)
clicky uninstall   # remove that again
```

### KDE: first run

Clicking goes through the desktop's **Remote Desktop portal**, so no root access or udev rules are needed.

- On the first start, KDE asks whether clicky may control input. **Allow it and share all monitors.**
- clicky saves a restore token, so later starts don't ask again.
- While clicky runs, Plasma may show a "remote control" indicator.

The hotkey is registered with KDE's global shortcuts. It also appears under System Settings → Keyboard → Shortcuts.

## Config

The config lives in `%APPDATA%\clicky\config.json` (Windows) or `~/.config/clicky/config.json` (Linux). It is created on first run, with `columns` and `rows` picked to give roughly 60×60-pixel cells on your smallest monitor (32×18 on 1920×1080). Those numbers are written once and never recalculated, so change them if you want a different grid. Restart clicky after editing it.

```json
{
  "hotkey": "Ctrl+Alt+Shift+M",
  "alphabet": "abcdefghijklmnopqrstuvwxyz0123456789",
  "columns": 32,
  "rows": 18,
  "subGridSize": 4,
  "fontSize": 0,
  "clickDelayMs": 60
}
```

- **hotkey:** modifiers `Ctrl`, `Alt`, `Shift` and `Meta`/`Win`, plus one key: `A`–`Z`, `0`–`9`, `F1`–`F24` or `Space`. Neither OS's shortcut API tells left Shift from right Shift.
- **alphabet:** the row and column characters. With `columns` and `rows` at `0`, its length sets the grid size.
- **columns:** columns on each monitor, using the first that many alphabet characters. `0` uses the whole alphabet.
- **rows:** rows on each monitor. Each monitor takes the next that many alphabet characters, in monitor order, so `rows` × the number of monitors can't be more than the alphabet length. `0` splits the whole alphabet between the monitors.
- **subGridSize:** the chosen cell is split into this many × this many squares, from `2` to `10`. The numbers always have two digits (`00`–`99`).

Bigger cells make the labels easier to read. Square cells use the space best: on a 1920×1080 screen, 32×18 gives 60×60-pixel cells, where the whole alphabet (`0`/`0`) gives 53×30. With a smaller grid, it helps to reorder `alphabet` so the keys that are easiest to reach come first.
- **fontSize:** label size in pixels; `0` fits the label to the cell.
- **clickDelayMs:** how long to wait after hiding the grid before clicking.

Logs and the portal token are kept in `%LOCALAPPDATA%\clicky\` or `~/.local/share/clicky/`.

## Layout

```
src/Clicky/
  Program.cs          CLI commands and the daemon (Avalonia app)
  Config.cs           config.json, file locations, logging
  Hotkey.cs           hotkey parsing and its KDE/Windows key codes
  Grid.cs             labels: monitors, rows, columns, sub-cells, typing rules
  Overlay.cs          one transparent full-screen window per monitor; keyboard input
  GridView.cs         drawing the grid
  Ipc.cs              single instance and `clicky show` (named pipe)
  Platform.cs         IPlatform: the OS-specific parts
  KdePlatform.cs      kglobalaccel + RemoteDesktop portal over D-Bus, XDG autostart
  DBus.cs             D-Bus method calls and signal subscriptions for KdePlatform
  WindowsPlatform.cs  RegisterHotKey, SetCursorPos + SendInput, Run-key autostart
tests/Clicky.Tests/   xUnit + Avalonia.Headless
tools/dbus-mock/      mock kglobalaccel + portal for testing KdePlatform without KDE
```
