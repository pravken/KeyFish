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
