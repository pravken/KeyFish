<img src="tools/fish-preview.png" width="300" align="right">

# KeyFish

[Русская версия](README.ru.md)

A tiny Windows tray app that fixes text typed in the wrong keyboard layout — for everyone who switches between a Latin and a Cyrillic layout.

**Languages:** English, German, Polish, Serbian Latin ⇄ Russian, Ukrainian, Belarusian, Serbian Cyrillic. KeyFish uses the layouts installed in Windows; with several layouts of one alphabet it picks the language from the word and the words before it.

- **Auto-correction as you type:** `ghbdtn vbh` → «привет мир», `руддщ` → "hello", `f vj;yj` → «а можно»
- **Case fixing:** "hELLO" (accidental CapsLock) → "Hello", "HEllo" → "Hello"
- **Text replacements:** `thx` + space → "thank you" — your own list in Settings
- **Pause** or **Ctrl+Shift+Space** — switch the layout of selected text; Pause or Ctrl+Z right after an auto-fix undoes it
- **Shift+F3** — cycle the case of selected text, like in Word
- **Shift+Pause** — transliteration: «Привет» ⇄ "Privet"
- Typo fixing via Windows spell check (experimental, turn it on in Settings)
- The tray fish shows the current layout: blue — Latin, orange — Cyrillic

If you erase a correction with Backspace, KeyFish remembers the word and never touches it again; if you fix a word yourself with Pause, it learns to fix it next time. It stays quiet in games and full-screen apps. All hotkeys can be changed in Settings (double-click the fish). KeyFish updates itself in one click. The interface is in English or Russian.

KeyFish is free. If it saves you time, you can [buy me a coffee on Ko-fi](https://ko-fi.com/pravken) ☕

## Languages: how it decides

KeyFish fixes a word only when it is typed in the layout of the *other alphabet*: Latin ⇄ Cyrillic. It doesn't switch between, say, English and German — both are Latin, and the same keys type nearly the same letters there.

For every word KeyFish compares what you typed with what the same keys give in your other installed layouts, using word lists and letter statistics of each language. Words it isn't sure about are left alone.

Serbian is written in both alphabets, so KeyFish treats a correct Serbian Latin word as your choice and never converts it to Cyrillic. Serbian Cyrillic is checked more strictly, by dictionary only: its layout is phonetic, so an English word typed in it (hello → хелло) can look plausible.

## Install

Download `KeyFish.exe` from [Releases](../../releases/latest) and run it — no installation needed. Turn on autostart in Settings.

Windows may show "Windows protected your PC" on first run because the app isn't signed. Click "More info" → "Run anyway".

## Privacy

KeyFish has to see your keystrokes to fix them, so here is exactly what it does with them:

- **Nothing is logged.** Only the word you're typing and the last few words are kept in memory, and they're dropped when you click, move the cursor or switch windows.
- **Nothing about your typing leaves your computer.** The only network request is the update check: KeyFish asks GitHub for the latest release of this repository. Spell checking uses the one built into Windows.
- **Password fields are off-limits.** In a password field of any app — browsers included — KeyFish doesn't correct or remember anything. The check runs in a small separate process, so Task Manager shows two `KeyFish.exe`: the second one (`--password-guard`) only asks Windows UI Automation whether the focused field is a password field, and it closes together with KeyFish.
- **What's stored on disk** is only what you ask for: your replacements, the lists of words to never or always correct (next to `KeyFish.exe`), and settings plus the mistake counter in the registry under `HKCU\Software\KeyFish`.
- **Updates are verified.** One-click update downloads only from this repository's releases and checks the SHA-256 checksum published by the release build before replacing the app. Releases are built by GitHub Actions straight from the source code in this repository.

## If KeyFish crashes

Please open an issue and say what you were doing (which app or site, which key you pressed). If it happens again, double-click `tools/crash-dumps-on.reg` (needs administrator rights): Windows will then save a small crash dump to `%LOCALAPPDATA%\CrashDumps` the next time, which shows exactly where it failed. The dump can contain the last few words you typed, so send it privately rather than attaching it to a public issue. `tools/crash-dumps-off.reg` turns this off.

## Build

Windows only: the C# compiler ships with .NET Framework 4.

```
build.bat
```

The icons are drawn by `tools\make-icon.ps1`. The version lives in `Version.cs`, the history in `CHANGELOG.md` (English) and `CHANGELOG.ru.md` (Russian).

## Credits

The frequency dictionaries in `data/` are built from [FrequencyWords](https://github.com/hermitdave/FrequencyWords) by Hermit Dave; the Belarusian word list comes from the [Belarusian AOSP dictionary](https://codeberg.org/Anibyl/Belarusian-AOSP-dictionary) by Anibyl. Both are licensed under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), and the files in `data/` are distributed under the same terms. `tools/build-dictionaries.ps1` rebuilds them from the sources.
