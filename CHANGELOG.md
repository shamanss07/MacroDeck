# Changelog

[English](CHANGELOG.md) · [Português (Brasil)](CHANGELOG.pt-BR.md)

Only public releases are listed here. Unreleased development changes do not receive a public version entry.

## 1.1.4 — macro save and playback settings fixes

- Save playback settings to the active macro only, without changing settings in other saved macros.
- Allow saving edited playback settings as a new macro when its name changes.
- Preserve macro actions and saved playback settings when overwriting or creating a macro copy.

## 1.1.3 — smoother transitions and theme fixes

- Add smoother window, page, and menu transitions, including minimize, restore, and maximize animations.
- Prevent white flashes in the Menu dropdown and keep rounded panels clean while themes redraw.
- Reduce cursor lag at startup by activating mouse capture only during recording.
- Stabilize the light/dark theme fade and update the Options window colors with the selected theme.

## 1.1.2 — repeat toggle label fix

- Let the repeat toggle use the available panel width so its English label is not clipped.

## 1.1.1 — interface alignment and font fixes

- Center text vertically in input fields and align unit labels with their values.
- Center the stepper arrows and keep them aligned across different control sizes.
- Apply the selected font consistently to the interval controls and reopened dialogs.

## 1.1.0 — English language support

- Add English and Brazilian Portuguese interface translations, with language changes applied immediately.
- Detect the Windows UI language as the default on first launch; a saved app preference takes precedence.
- Provide English as the main project documentation and keep Portuguese documentation available.
- Fix the Options dialog clipping and make the Windows CI workflow locate MSBuild reliably.
- Build and attach a versioned Windows executable to tagged GitHub releases.

## 1.0.0 — first public release

- Record, preview, save, and play back keyboard and mouse macros.
- Configurable global shortcuts and repeat interval controls.
- Macro library with folder selection, refresh, and confirmation-protected deletion.
- Light and dark themes, custom colors, installed fonts, and persistent preferences.
- Windows Forms interface with the MacroDeck app icon.
- Resolution-independent interface scaling, adjusted for available space and monitor/DPI changes.
- Initial support for Windows and .NET Framework 4.8.

---

[Project README](README.md) · [Português (Brasil)](CHANGELOG.pt-BR.md)
