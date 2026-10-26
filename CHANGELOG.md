2.0.4
• crash.log next to KeyFish: its own errors with the place in its code, and Windows' record of a crash, copied the next time KeyFish starts. No typed text, nothing is sent anywhere; attach it to an issue if KeyFish crashes
• An unexpected error no longer shows .NET's error box: it goes to crash.log and KeyFish keeps working

2.0.3
• Choose the theme in Settings: as in Windows, light or dark
• Never correct and Always correct ignore punctuation around a word: "word," and "word" are now one entry, and entries saved with a comma or a period work again
• Scrollbars of the lists follow the dark theme

2.0.2
• Check for updates right from Settings or About: the status shows whether your version is the latest
• Optional: the tray shows the layout's letters (EN, RU…) in a circle instead of the fish
• A color of your own for each layout: the fish, the letters and the tag at the cursor use it; on a light color the letters turn dark
• Settings show small looping pictures of what each option does, with your own layouts, colors and a word of your language
• Text and hotkey fields are rounded like the buttons; the interface language is near the top of Settings; layout colors go into the settings export
• What's new shows ⇄ and 🐟 instead of empty squares

2.0.1
• Password fields: the check could count as done before it had answered (on a PC running for 25 days or more, or while the check was still starting). Fixed, and checks are now timed more strictly
• Hotkeys (Pause, double Shift, Alt+Pause and the others) don't work in password fields and in excluded apps: in a terminal they used to send Ctrl+C and stop the running command
• Settings, About and messages scale properly on 125–200 % screens, and the Settings window fits on small screens
• A line break in a replacement is typed as Shift+Enter, so a chat doesn't send the message halfway
• Undoing a fix made with Enter puts the original back in the input box instead of sending it again
• Typos are checked without holding up your typing; spell checkers load in advance for all your languages
• Store apps (Mail, WhatsApp and others) can be excluded and get a layout of their own
• Fixed: the right Ctrl could stay pressed after undoing with Ctrl+Z; words over 40 letters were fixed wrongly; holding a modifier for a second made KeyFish release it; in slow apps a hotkey could paste the old clipboard instead of the fix
• While the password-field check is starting, the fish is grey and says so; the tray menu follows a change of the Windows theme
• KeyFish's messages match its windows, menu text is smoothed like the rest, and Narrator reads the settings switches, sections and lists
• Safer: Windows DLLs are loaded only from the system folder; after an update the previous version is kept for a minute in case the new one fails to start
• The logo's bubbles rise and pop in a smooth loop; an About button in Settings; Support with a red heart

2.0.0
• 19 languages: English, German, Polish, French, Spanish, Italian, Portuguese, Czech, Turkish, Serbian Latin ⇄ Russian, Ukrainian, Belarusian, Bulgarian, Macedonian, Serbian Cyrillic, Greek, Hebrew, Georgian. KeyFish uses the layouts installed in Windows and fixes words typed in a layout of another alphabet
• Works with any layout arrangement (QWERTZ, AZERTY, letters typed with AltGr or with accent keys like Greek ΄ or French ^): keys are tracked by their position on the keyboard
• With several layouts installed, KeyFish picks the language from the word and the words before it, so a Ukrainian word goes to the Ukrainian layout
• Serbian is written in both alphabets, so a correct Serbian Latin word is never "fixed" into Cyrillic; Serbian Cyrillic, Macedonian and Greek layouts are phonetic, so words typed in them are fixed only when they are in the dictionary
• Layout per app: pick a layout for Telegram, VS Code and so on, and KeyFish switches to it when you go to that app
• New Settings in the Windows 11 style, with a dark theme that follows Windows; replacements, apps and words are now tables you add to and delete from, instead of plain text
• The interface is also in Ukrainian, Belarusian, German, Polish, Serbian, French and Spanish
• Sharp on high-DPI screens; the tray menu follows the Windows theme
• The fish is now drawn in detail, and the About window hides a little surprise 🐟
• Pause on selected text converts it to your other installed layout, not just English ⇄ Russian
• A Support button at the top of Settings and in the About window
• Smoother text in all windows, in Windows 11's own font
• Fix the last word with one key: double Shift or Caps Lock (off by default; Shift+Caps Lock still turns capitals on)
• The layout can show next to the text cursor for a moment when it changes (off by default)
• The fish menu: pause corrections for an hour, and recent corrections — click one and KeyFish won't correct it that way again. While KeyFish isn't correcting, the fish is grey
• Import replacements and word lists from other programs: plain text, CSV/TSV, AutoHotkey hotstrings, Espanso
• All settings in one file: Export and Import in Settings. Before every save KeyFish keeps a copy of the previous settings in the backups folder (the last 10)
• Learns from a manual fix too: erase a word, switch the layout and type the same keys again, and next time KeyFish fixes that word by itself
• A word where the layout was switched in the middle (ghиветик) is fixed whole
• Links, paths and code are left alone: "://", "www.", slashes and other symbols inside a word; file.txt and camelCase change only into a dictionary word
• Alt+Pause switches the layout of the whole line before the cursor
• An optional soft sound on each correction
• More reliable: if Windows quietly turns off KeyFish's keyboard watching (it does that to a program that answered too slowly once), KeyFish notices and turns it back on; files are read and written in the background so typing is never held up
• Terminals, SSH clients, virtual machines, remote desktops and more password managers are excluded by default (added to your list once)
• Text KeyFish puts on the clipboard stays out of clipboard history (Win+V) and cloud sync
• Fixed: a backslash in a replacement (C:\new) turned into a line break after saving; abbreviations with digits (addr1) didn't expand; words KeyFish learned while Settings was open were lost on Save; letters typed very fast right after a correction could lose Shift; in VS Code and Visual Studio, Pause with nothing selected added a converted copy of the whole line
• Fixed: with many heavy windows open (browsers, Electron apps, editors), KeyFish could correct nothing for up to a minute or two after it started — the password-field check was still getting ready. It now answers right away
• To find out why a word wasn't corrected, create an empty debug-log.txt next to KeyFish.exe: KeyFish writes the app and the reason for every word, never the word itself (see the README)
• KeyFish starts even from a read-only folder such as Program Files: its files then go to %APPDATA%\KeyFish
• Each release comes with a signed build attestation (see the README)

1.8.3
• New About window: version, statistics and a link to the project; the list of changes now opens from a separate What's new button

1.8.2
• Fixed a crash on some login pages and windows: the password-field check (Windows UI Automation) now runs in a small separate process, so a failure there can no longer close KeyFish — the check simply restarts
• tools/crash-dumps-on.reg helps report a crash (see the README)

1.8.1
• Fixed: autostart silently did nothing if it pointed to a KeyFish.exe that was moved or deleted. KeyFish now repairs the entry on start, and Settings shows autostart as off when it can't work

1.8.0
• Ctrl+Z right after an auto-fix undoes it, just like Pause; the next Ctrl+Z works as usual in the app and undoes text (also when you hold Ctrl and press Z twice). Can be turned off in Settings

1.7.0
• Update in one click: KeyFish downloads the new version from GitHub, verifies its checksum and restarts
• Learns from you: a word you fix yourself with Pause gets corrected automatically next time
• Quiet mode: no corrections in games and full-screen apps
• Password fields are left alone in every app, browsers included — nothing is corrected or remembered there
• Statistics: how many mistakes the fish has caught
• Releases are now built automatically by GitHub Actions

1.6.0
• New name — KeyFish 🐟 (formerly LayoutFix); settings and autostart carry over automatically
• The tray fish shows the layout: blue — EN, orange — RU; it blinks after an auto-fix
• Text replacements: "thx" + space → "thank you", work even when typed in the other layout
• Transliteration of selected text: Shift+Pause (привет ⇄ privet)
• Settings window: toggles, your own hotkeys, replacements and exclusion lists
• Update check on GitHub
• English interface (chosen in Settings; defaults to the Windows language)

1.5.0
• Typo fixing via Windows spell check (off by default)

1.4.0
• Short words before a corrected word are fixed too: "f vj;yj" → "а можно"
• Erase a correction with Backspace and KeyFish remembers the word and leaves it alone
• The list of remembered words can be opened from the menu

1.3.0
• Layout auto-correction while typing
• Case fixing: "hELLO" (accidental CapsLock) → "Hello", "HEllo" → "Hello"
• Pause right after an auto-fix undoes it
• Excluded apps

1.2.0
• Fixed: after Ctrl+A part of the text stayed in the old layout

1.1.0
• Shift+F3 cycles the case of selected text, like in Word

1.0.0
• Pause or Ctrl+Shift+Space switches selected text between EN and RU layouts
