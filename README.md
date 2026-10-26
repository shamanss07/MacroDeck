# MacroDeck

[English](README.md) · [Português (Brasil)](README.pt-BR.md)

A Windows macro recorder and player.

## Credits

Developed by [shamanss07](https://github.com/shamanss07), with implementation and review assistance from ChatGPT (OpenAI).

![MacroDeck logo](Source/Assets/Branding/MacroDeck-Logo.png)

## Features

- Record and play back keyboard and mouse actions.
- Preview a recording before saving it, and organize macros as JSON files.
- Configure global shortcuts, repetition, and the delay between cycles.
- Use light and dark themes, customize colors, and select installed fonts.
- Keep preferences between sessions.
- Choose Portuguese (Brazil) or English for the app interface in **Menu → Settings**; changes apply immediately. On first launch, MacroDeck uses the Windows UI language when it is English and otherwise defaults to Portuguese (Brazil).
- Includes a placeholder for the future Auto-Clicker feature.

## Compatibility

The initial release supports **Windows only**. MacroDeck is built with C# and Windows Forms and requires **.NET Framework 4.8**. Linux and macOS are not supported in this release.

## Install and use

Download `MacroDeck-1.1.4.exe` from the GitHub **Releases** page and run it on Windows. If .NET Framework 4.8 is not installed, Windows may ask you to install it.

1. Choose the folder where you want to store macros.
2. Enter a name and click **Record**. The default shortcut is F9.
3. Click **Record** again or press F9 to stop.
4. Click **Play** to preview the recording, or click **Save recording** to keep it.
5. Select a saved macro and click **Play**. The default shortcut is F10.

Use **Set** to change a shortcut; Esc cancels shortcut capture. **Repeat indefinitely** keeps playing until you stop it. The delay controls accept minutes and seconds.

Macros are JSON files in the selected folder. Preferences are stored in `%APPDATA%\MacroDeck\settings.json`. Closing the app discards an unsaved recording without prompting.

## Build from source

1. Install the .NET Framework 4.8 Developer Pack on Windows.
2. Open PowerShell in the `Source` folder.
3. Run:

   ```powershell
   .\build.ps1 -OutputName MacroDeck-1.1.4.exe
   ```

The executable is created in the project root. You can also open `Source/MacroDeck.csproj` in Visual Studio. The OFL license in `Source/Assets/Fonts/OFL.txt` covers the included Montserrat font, not MacroDeck's code.

## License

MacroDeck's code is released under the MIT License in `LICENSE`. The included Montserrat font is licensed under the SIL Open Font License 1.1 in `Source/Assets/Fonts/OFL.txt`.

---

Documentation: [Português (Brasil)](README.pt-BR.md) · [Privacy](PRIVACY.md) · [Contributing guide](CONTRIBUTING.md)
