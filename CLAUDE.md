# clicky: notes for Claude

The README covers what the app does, how to build and use it, and the code layout. This file holds what the README doesn't: why things are the way they are, the traps, and the current status.

## Status (2026-10-21)
- Builds with no warnings and passes 108 tests. **The user tested it on KDE Plasma 6 Wayland: the portal, the grid, focus and clicks all work as expected.** On Windows the user has run it: clicks work, except while an elevated window has the foreground (see below).
- Added after that test, at the user's request: a 4×4 sub-grid inside the chosen cell, typed as two digits `00`–`15`, so a full label is 4 keys (`Grid.Resolve` accepts 2 or 4 chars, and `Grid.CanAppend` holds the typing rules). `Enter`/`Space` clicks the cell centre. The user tried 3×3 (`1`–`9`, too coarse) and 5×5 (`00`–`24`, labels didn't fit in the cell) before settling on 4×4 as the default. Since 2026-10-20 the size is the `subGridSize` config setting (2–10; labels stay two digits).
- **Overlay legibility (user feedback):** labels are 50% opacity and small, the screen tint is light, and nothing may cover the chosen cell. Sub-cell labels go inside the cell when they fit at 8 px or larger (`GridView.MinSubFont`); otherwise the cell gets lines only and a magnified key beside it carries the labels.
- **Grid size is configurable (2026-10-20):** `columns` and `rows` (per monitor) take alphabet prefixes; `0` keeps the old behaviour (all 36 columns, alphabet split across monitors). Requested because the 36×36 default is hard to read on a 1920×1080 laptop (53×30 px cells); 32×18 gives 60×60 px cells. The user chose explicit counts over an automatic target cell size so labels never shift on their own. Since 2026-10-21 a *new* config gets counts fitted once to the smallest monitor (`Config.FitGrid`, about 60 px cells) and written to the file; they are never recalculated. Because the screens are only known after Avalonia starts, `Config.Load` no longer writes a missing file: `Overlay.CreateConfigFile` does, at startup or on the first show. An existing file without the keys still gets `0/0/4`. The user tested a new config on KDE (2026-10-21) and it worked; not yet tried on Windows.
- **Windows UIPI fix (2026-10-20, untested on Windows):** clicks were dropped whenever an elevated window (admin PowerShell, the CatsEyeXI launcher) had the foreground; the user confirmed this. The overlay now goes blank and click-through (`IPlatform.MakeClickThrough`) and stays focused until `SendInput` has run. Still open: whether clicks *on* the elevated window itself get through. If not, the next step is an opt-in elevated autostart (a Task Scheduler task with `/RL HIGHEST`).
- The .NET SDK may not be installed: `sudo pacman -S dotnet-sdk`.
- **Rewritten for simplicity on 2026-10-11** (same behaviour): one flat `src/Clicky/` folder, one namespace, and a single `IPlatform` interface with `KdePlatform` and `WindowsPlatform`. The renders were pixel-identical to the old version, and the D-Bus mock run matched. The pre-rewrite source is in `../clicky-before-rewrite-2026-10-11.tar.gz`.

## Decisions (made with the user; don't revisit without asking)
- **.NET 10 + Avalonia 11.3.22**, chosen because the user wants as much .NET as possible. Avalonia 12.x exists, but 11.3 was chosen for a known API.
- **One codebase for Windows and KDE Plasma 6 Wayland.** The backend is picked at runtime (`Platform.Create` in `Platform.cs`), and Windows code is marked `[SupportedOSPlatform("windows")]`.
- **Multi-monitor:** a grid on every monitor with **unique labels**. The row alphabet is split across monitors, ordered left to right and then top to bottom, and each monitor keeps the same columns (all 36 by default). The user chose this over Tab-switching or a monitor prefix key.
- **KDE click:** the xdg **RemoteDesktop portal** with a ScreenCast monitor selection, used only for stream IDs; the PipeWire stream is never opened. It needs no root. The restore token is kept in `~/.local/share/clicky/portal.json`.
  - Rejected: uinput (needs a sudo udev rule and group, which the user had set up for v1 and then reverted) and KWin fake-input (needs a hand-written libwayland binding).
- **KDE hotkey:** kglobalaccel over D-Bus. Either Shift is fine (the user's original ask was LShift, which isn't feasible without raw evdev).
- **Overlay on Linux:** runs as an X11 window through XWayland, because Avalonia 11 has no Wayland backend.
- **v1 history:** v1 was Python + PyQt6 + uinput. It was deleted, and the user undid its system setup.

## Traps (each one bit once)
- **Tmds.DBus.Protocol must stay at 0.21.3.** That's the version Avalonia.FreeDesktop 11.3 is built against. Newer versions (0.9x) rename `Connection` to `DBusConnection` and crash Avalonia at startup with a TypeLoadException. Use `Connection`, `Address.Session`, `DBusException`, and `AddMatchAsync` through `DBus.WatchSignalAsync`.
- **`Environment.GetFolderPath` returns `""` when the folder doesn't exist.** That silently creates relative paths inside the current directory. Always go through `Paths` in `Config.cs`, which uses `DoNotVerify`.
- **kglobalaccel `setShortcut` signature** is `as, ai, u`. It's called twice, with flags `1` (IsDefault) and then `6` (SetPresent|NoAutoloading). The default hotkey's Qt key code is `0x0E00004D`.
- **UIPI drops `SendInput` while an elevated window is in the foreground**, with no reliable error. So clicky must not give up the foreground before it clicks on Windows; see `Overlay.Click`.
- **Win32 `INPUT` must be 40 bytes on x64.** `SendInput` silently fails otherwise, and `Win32LayoutTests` (in `PlatformTests.cs`) pins this.
- **Tests disable file logging** (`TestSetup.cs`) so they don't write into the real `~/.local/share/clicky`.
- When smoke-testing the CLI, point `XDG_CONFIG_HOME`/`XDG_DATA_HOME` at a temp dir so you don't touch the user's real config.

## Testing without a desktop
- `dotnet test` covers the grid maths, hotkey encodings, config, portal stream pairing, the Win32 struct layout, and a headless render. Set `CLICKY_SNAPSHOT_DIR=<dir>` to keep the PNGs of each input stage, plus a single-monitor shot showing the magnified-key fallback.
- `tools/dbus-mock/` contains a Python mock of kglobalaccel and the portal (`mock.py`, which needs python-dbus and python-gobject) plus a .NET harness that drives the real `KdePlatform`. Run it on a private bus:
  ```sh
  eval $(dbus-launch --sh-syntax); python3 tools/dbus-mock/mock.py &
  XDG_DATA_HOME=$(mktemp -d) dotnet run --project tools/dbus-mock
  kill %1 $DBUS_SESSION_BUS_PID
  ```
  The expected mock output: `setShortcut … 1` then `… 6`, the portal calls in order, `Motion 71 6.67 7.5` (the centre of sub-cell `00` in cell `sa`), then `Button 0x110 1/0`. A second run should send `restore_token`.
- The Windows build cross-compiles from Linux (see the README `dotnet publish` lines) but can only be *run* on Windows.

## Untested (verify on real hardware)
(Items 1–3 passed a first check on KDE, 2026-10-11. The restore token across a restart and mixed scaling were not checked explicitly.)
1. **KDE portal:** does the permission dialog appear, and does sharing all monitors work? After a restart, is there no prompt (restore token)? Does Plasma show a "remote control active" indicator for as long as the session lives? If that's annoying, open the session per click instead.
2. **Stream pairing:** streams are matched to Avalonia screens by position order (`KdePlatform.SortStreams`). Check that each grid clicks on the right physical monitor, including with mixed scaling.
3. **Focus:** does the XWayland overlay get keyboard focus straight away? The primary-screen window is activated, and any focused window forwards keys to `Overlay`. A mouse click on the overlay cancels it, as an escape hatch.
4. **Windows:** clicks while an elevated window is focused (on the taskbar, on other apps, and on the elevated window itself), `RegisterHotKey` firing, the grid on every monitor, click accuracy at 150% and mixed DPI, and autostart through the HKCU Run key.

## Ideas not done
- Shrink the about 95 MB single-file builds (trimming would need a source-generated JSON context).
- Right-click and double-click variants, for example a modifier on the second key.
- Run `GridView` text drawing through `FormattedText` caching if rendering turns out to be slow.
