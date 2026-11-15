# Changelog

What's new in each version. The release page on GitHub shows the section of its version. Earlier versions: [Releases](https://github.com/otazniksk/ShotLite/releases).

## 1.2.4

### Changed
- About ShotLite links to the new website, [otazniksk.github.io/ShotLite](https://otazniksk.github.io/ShotLite/), and so does ShotLite under Settings → Apps in Windows.

## 1.2.3

### Added
- Full size in the aspect ratio menu: selects the whole monitor the selection is on, not all monitors.

### Fixed
- GIF recordings keep the colors of small details that appear later, such as a red arrow or a colored button; before, they could turn into a nearby color (red came out orange). File size stays the same.
- Text in copied and saved screenshots no longer has a dark fringe around the letters.

## 1.2.2

### Fixed
- On Windows 11, `PrtScr` could keep opening the Windows screen capture instead of ShotLite, without any warning: Windows uses the key for itself by default. ShotLite now detects it and asks to turn the Windows setting off (no administrator rights needed); the tray menu offers the same fix in red. It works with notifications turned off.

## 1.2.1

### Fixed
- Installing an update from the tray menu no longer closes the menu without feedback: it shows the download progress, then **Restart and update** installs it when you click it. If it fails, one click tries again.

## 1.2.0

### New
- **Copy text from the screen (OCR):** select an area and press the OCR button or `Ctrl+Shift+C`; the text goes to the clipboard. It uses the recognition built into Windows, so it works offline and nothing leaves your PC. It reads the languages installed in Windows; black-outs are applied first, so hidden text is never copied.
- **Text size:** hold or right-click the Text button to choose 10–64 px, or turn the mouse wheel with the Text tool. The size no longer depends on the line width and is remembered.

### Improved
- New text starts exactly at the I-beam cursor instead of below it.
- The keyboard shortcuts work while the settings are open, so you can take a screenshot of them. They pause only while you type a new shortcut.

### Fixed
- Colors, sizes and styles changed in a screenshot taken while the settings were open are no longer reset by confirming the settings.
- An update no longer restarts the app while text recognition is running.
