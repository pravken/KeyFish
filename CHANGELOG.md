2.3.0 2026-10-30
New:
• Statistics in a Settings section of their own, with a chart for the last 30 days and a reset button
• In the text actions menu: typo correction, typographic quotes, non-breaking spaces, the letter ё
• "Do not correct in" the current app from the fish menu

2.2.1 2026-10-30
Fixed:
• Hotkeys did not work while the Settings window was open

2.2.0 2026-10-30
New:
• A text actions menu on Ctrl+Shift+F2: case, layout, spaces, lines, a name for code
• Calculating an expression in the selected text
• Paste without formatting on Ctrl+Shift+V in any app
Improved:
• A new layout of the About window

2.1.5 2026-10-29
Improved:
• Remote desktop clients other than mstsc are excluded by default
Fixed:
• A crash with the clipboard of a remote desktop session

2.1.4 2026-10-29
Fixed:
• The week and month statistics in About counted only the days recorded so far

2.1.3 2026-10-29
New:
• Settings apply as soon as they are changed
• Each layout from the third one on gets a color of its own
Improved:
• Settings sections reordered: Correction first, System last
• The same buttons in all lists
• Layout colors and the layout icons in one setting
• Formal wording throughout the interface
• Pictures in Settings play once and follow the Windows animation setting
• Unavailable controls are clearly grey

2.1.2 2026-10-29
Improved:
• Extended dictionary: a switch and a picture, like the other settings
Fixed:
• A look chosen during a holiday didn't apply

2.1.1 2026-10-29
New:
• A Windows notification with an "Undo the fix" button on the first corrections
• A custom look: an accent color and a fish color
Improved:
• Disabled switches look inactive
• A warning about stored hotkeys that can no longer be used
Fixed:
• A click in another window no longer prevents undoing the last fix
• Double Shift sometimes found no word to fix

2.1.0 2026-10-29
New:
• Looks: a gallery with a preview before applying, a Halloween look
• Sounds: a sound for each event, your own .wav files too
• Shift+F2 clears the formatting of selected text
• "Why wasn't it corrected?" in the fish menu
• Pressing Pause again goes through all installed layouts
• A hotkey to pause corrections
• A hotkey for the text in the clipboard
• Statistics in About: today, week, month, time saved
• A short introduction on the first start
Improved:
• High contrast mode
• Transliteration for Ukrainian, Belarusian, Serbian and Greek
• Fish menu: the current layout and quick switches
• Settings in groups
• What's new: each version on its own, changes in groups
• Less memory for the dictionaries
• What KeyFish remembers about typing is cleared when the computer is locked
Fixed:
• Pressing Pause twice on a correct word no longer teaches KeyFish to change it
• Small fixes from an audit

2.0.5 2026-10-29
New:
• Extended dictionary, downloaded on request
• Optional typography: dash, ellipsis, © ® ™
• Notice in apps run as administrator
• Offer to report a crash on the next start
• Tip on the first corrections
Improved:
• More accurate detection: contractions, abbreviations, punctuation
• MIT license

2.0.4 2026-10-26
New:
• Crash log (crash.log)
Fixed:
• Errors no longer interrupt work with a .NET error box

2.0.3 2026-10-25
New:
• Theme choice: system, light, dark
Improved:
• Dark scrollbars in the dark theme
Fixed:
• Word lists ignore punctuation

2.0.2 2026-10-22
New:
• Update check in Settings and About
• Layout letters in the tray instead of the fish (optional)
• Custom layout colors
• Illustrations for settings
Improved:
• Minor interface improvements

2.0.1 2026-10-22
Improved:
• Correct scaling on 125–200% displays
• Line breaks in replacements via Shift+Enter
• Microsoft Store apps in exclusions and per-app layouts
• Narrator support
• System DLLs load only from the Windows folder
Fixed:
• Password field detection
• Hotkeys disabled in password fields and excluded apps
• Undoing a fix after Enter no longer resends text
• Typo checking no longer delays typing
• Stuck Ctrl, long words, and pasting stale clipboard contents

2.0.0 2026-10-21
New:
• 19 languages: English, German, Polish, French, Spanish, Italian, Portuguese, Czech, Turkish, Serbian Latin ⇄ Russian, Ukrainian, Belarusian, Bulgarian, Macedonian, Serbian Cyrillic, Greek, Hebrew, Georgian
• Any key arrangement: QWERTZ, AZERTY, AltGr, accent keys
• Layout per app: Telegram in Russian, VS Code in English
• New Settings in the Windows 11 style, with a dark theme
• Interface in Ukrainian, Belarusian, German, Polish, Serbian, French and Spanish
• Fix the last word with double Shift or Caps Lock (optional)
• The layout next to the text cursor when it changes (optional)
• Fish menu: pause for an hour and recent corrections
• Import replacements and word lists: text, CSV/TSV, AutoHotkey, Espanso
• Export and import all settings as one file; a copy before every save
• Alt+Pause switches the layout of the whole line before the cursor
• A soft sound on corrections (optional)
• debug-log.txt: the reason for every word, never the word itself
• A signed build attestation for each release
Improved:
• The language is picked from the word and the words before it
• Serbian Latin is never "fixed" into Cyrillic; phonetic layouts only by dictionary
• Pause converts to any installed layout, not just English ⇄ Russian
• Learns from a manual fix too: erase, switch the layout, type again
• A word with the layout switched in the middle is fixed whole
• Links, paths and code are left alone
• Sharp on scaled screens, the menu in the Windows theme, a new fish
• Smooth text in Windows 11's font
• A Support button in Settings and About
• KeyFish turns keyboard watching back on if Windows turns it off
• Terminals, SSH, virtual machines, remote desktops and password managers excluded by default
• KeyFish's text stays out of clipboard history (Win+V) and cloud sync
• Starts from a read-only folder: files then go to %APPDATA%\KeyFish
Fixed:
• A backslash in a replacement turned into a line break
• Abbreviations with digits didn't expand
• Words learned while Settings was open were lost on Save
• Shift was lost when typing very fast right after a fix
• Pause with nothing selected duplicated the line in VS Code and Visual Studio
• With many heavy windows open, KeyFish corrected nothing for up to two minutes after starting

1.8.3 2026-10-21
New:
• About window: version, statistics, a link to the project; What's new opens from its own button

1.8.2 2026-10-21
New:
• tools/crash-dumps-on.reg to report a crash
Fixed:
• A crash on some login pages: the password-field check runs in a separate process

1.8.1 2026-10-20
Fixed:
• Autostart did nothing if KeyFish.exe was moved or deleted; the entry is now repaired on start

1.8.0 2026-10-18
New:
• Ctrl+Z right after an auto-fix undoes it, like Pause (can be turned off)

1.7.0 2026-10-17
New:
• Update in one click, with a checksum check
• Learns from you: a word you fix with Pause is corrected by itself next time
• Quiet mode in games and full-screen apps
• Statistics: how many mistakes the fish has caught
Improved:
• Password fields are left alone in every app, browsers included
• Releases are built by GitHub Actions

1.6.0 2026-10-17
New:
• New name — KeyFish 🐟 (formerly LayoutFix); settings and autostart carry over
• The tray fish shows the layout and blinks after a fix
• Text replacements: "thx" + space → "thank you"
• Transliteration of selected text: Shift+Pause
• Settings window: toggles, hotkeys, replacements, exclusions
• Update check on GitHub
• English interface

1.5.0 2026-10-16
New:
• Typo fixing via Windows spell check (optional)

1.4.0
New:
• Short words before a corrected word are fixed too: "f vj;yj" → "а можно"
• A correction erased with Backspace is remembered and not repeated
• The list of remembered words in the menu

1.3.0
New:
• Layout auto-correction while typing
• Case fixing: "hELLO" → "Hello", "HEllo" → "Hello"
• Pause right after a fix undoes it
• Excluded apps

1.2.0
Fixed:
• After Ctrl+A part of the text stayed in the old layout

1.1.0
New:
• Shift+F3 cycles the case of selected text

1.0.0
New:
• Pause or Ctrl+Shift+Space switches selected text between EN and RU
