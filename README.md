# clicky

Press a hotkey (default **Ctrl+Alt+Shift+M**) and every monitor gets a labelled grid. Type a cell's two characters, then a digit `1`–`9` to pick a spot inside that cell, and clicky moves the mouse there and left-clicks.

It runs on **Windows** and **KDE Plasma 6 (Wayland)** and is written in .NET 10 with Avalonia.

## How labels work

- The first character picks the row and the second picks the column.
- Labels are unique across monitors. The 36 row characters (`a`–`z`, `0`–`9`) are split between monitors, ordered left to right and then top to bottom. Every monitor keeps all 36 columns.

| Monitors | Rows on each monitor |
|---|---|
| 1 | `a`–`9`: a 36×36 grid |
| 2 | `a`–`r`, then `s`–`9`: 18×36 each |
| 3 | `a`–`l`, `m`–`x`, `y`–`9`: 12×36 each |

- After you type the first character, its row lights up and everything else dims.
- After the second character, the chosen cell shows a 3×3 sub-grid numbered `1`–`9` in reading order (`1` top-left, `5` centre, `9` bottom-right). Type a digit to click the centre of that sub-cell, or press `Enter`/`Space` to click the centre of the whole cell.
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

The config lives in `%APPDATA%\clicky\config.json` (Windows) or `~/.config/clicky/config.json` (Linux). It is created on first run. Restart clicky after editing it.

```json
{
  "hotkey": "Ctrl+Alt+Shift+M",
  "alphabet": "abcdefghijklmnopqrstuvwxyz0123456789",
  "fontSize": 0,
  "clickDelayMs": 60
}
```

- **hotkey:** modifiers `Ctrl`, `Alt`, `Shift` and `Meta`/`Win`, plus one key: `A`–`Z`, `0`–`9`, `F1`–`F24` or `Space`. Neither OS's shortcut API tells left Shift from right Shift.
- **alphabet:** the row and column characters. Its length sets the grid size.
- **fontSize:** label size in pixels; `0` fits the label to the cell.
- **clickDelayMs:** how long to wait after hiding the grid before clicking.

Logs and the portal token are kept in `%LOCALAPPDATA%\clicky\` or `~/.local/share/clicky/`.

## Layout

```
src/Clicky/Core/        config, hotkey parsing, grid layout, single-instance pipe
src/Clicky/Overlay/     one transparent full-screen window per monitor
src/Clicky/Platform/
  Windows/              RegisterHotKey, SetCursorPos + SendInput, Run-key autostart
  Linux/                kglobalaccel + RemoteDesktop portal (Tmds.DBus.Protocol), XDG autostart
tests/Clicky.Tests/     xUnit + Avalonia.Headless
tools/dbus-mock/        mock kglobalaccel + portal for testing the KDE backend without KDE
```
