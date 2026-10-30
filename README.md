# ShotLite

**English** · [Slovak](README.sk.md)

A lightweight tool for screenshots and screen recording on Windows. It was created as a replacement for the no longer supported **Lightshot 5.5.0.7**. It works the same way (PrtScr → select an area → draw → copy/save), but **without telemetry and without uploading to prntscr.com**. On top of that it records the screen to MP4 or GIF.

<p align="center">
  <img src="docs/screenshot.png" alt="ShotLite editor: selection with arrows, rectangle, black-out, frosted glass and highlighter" width="640">
</p>

## Download

- **Installer (recommended):** [ShotLite-install.exe](https://github.com/otazniksk/ShotLite/releases/latest/download/ShotLite-install.exe). Installs for the current user, no administrator rights needed.
- **Portable:** [ShotLite-portable.zip](https://github.com/otazniksk/ShotLite/releases/latest/download/ShotLite-portable.zip). Unzip and run `ShotLite.exe`.
- All versions and release notes: [Releases](https://github.com/otazniksk/ShotLite/releases).

Windows 10/11 x64. The .NET runtime is included. Screen recording needs Windows 10 version 2004 or newer.

The installed app offers new versions in the tray menu and installs them with one click, or automatically if you turn that on in the settings. Every update is checked against a digital signature before it runs. The portable version links to the download instead.

## Controls

| Action | Control |
|---|---|
| Select an area | `PrtScr`, or left-click the tray icon |
| Save the full screen (to the folder) | `Shift+PrtScr` |
| Copy the full screen | `Ctrl+PrtScr` |
| Record the screen to MP4 or GIF, stop it the same way (Windows 10 2004+) | `Ctrl+Shift+PrtScr`, then press Record whole monitor, or select an area and press `Enter`; or the red button in the overlay (`Ctrl+R`). Hold the button for format and cursor |
| Aspect ratio of the selection (screenshots and recordings) | `V`, or the ratio button; hold it to choose. Ratios and the default are in the settings |
| Whole monitor under the cursor | `Ctrl+A` in the overlay |
| Move / resize the selection | drag inside the selection / drag the handles |
| Tools | `P` pencil, `L` line, `A` arrow, `R` rectangle, `B` black out, `M` highlighter, `T` text |
| Black-out style (black / frosted glass / pixelate) | hold or right-click the Black out icon; the choice is remembered |
| Line width / text size | mouse wheel |
| Straight line at 45° / square | hold `Shift` while drawing |
| Undo | `Ctrl+Z` |
| Copy | `Ctrl+C`, `Enter`, or click outside the selection |
| New selection | drag outside the selection (while nothing is drawn) |
| Save | `Ctrl+S` |
| Print | `Ctrl+P` |
| Upload (if configured) | `Ctrl+U` |
| Cancel | `Esc` or right-click |

Text: click inside the selection with the Text tool, type, confirm with `Enter` (`Shift+Enter` = new line, `Esc` = cancel).

## Screen recording

- A selected area or one whole monitor, without sound.
- MP4 (H.264, 30 fps) or GIF (15 fps, up to 800 px wide and 60 seconds long).
- Choose the format and whether to record the cursor by holding the red button; the default format is also in the settings.
- Recordings go to `Videos\ShotLite`, and the folder opens when you stop. Both can be changed in the settings, separately from screenshots.
- The frame and the bar with the time and stop button stay out of the recording.

## Aspect ratios

A selection can be locked to an aspect ratio, for screenshots and recordings alike. The presets are 9:16, 4:3 and 16:9. You can change the list and pick a default in the settings.

## Languages

UI texts are in `languages\<code>.json` next to `ShotLite.exe`; the code is the ISO language and country name (e.g. `en-US`, `de-DE`, `sk-SK`). Included: English, Czech, Dutch, French, German, Hungarian, Italian, Polish, Portuguese (Brazil), Slovak, Spanish, Ukrainian, Chinese (Simplified), Japanese and Korean.

The settings offer every file found in the folder. On first start the language follows Windows (exact match first, then the same language, otherwise English). English is also embedded in the exe, so a missing key or file never breaks the UI.

To add a language: copy `en-US.json`, e.g. as `ro-RO.json`, translate the values and set `_language` (the name shown in the list). Do not change the keys. Translations other than English and Slovak were machine-assisted; corrections from native speakers are welcome.

## License

ShotLite is open source under the [MIT License](LICENSE).

## Testers

Thank you for testing ShotLite ❤️

[Arduinak](https://github.com/Arduinak), [Spacesov](https://github.com/Spacesov)
