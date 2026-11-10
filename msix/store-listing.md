# KeyFish in the Microsoft Store: submission texts

What goes into Partner Center for each submission. Product: KeyFish, Store ID 9P8BDBQM7G22.

## Properties

- **Category:** Utilities & tools
- **Privacy policy URL:** https://github.com/pravken/KeyFish/blob/main/PRIVACY.md
- **Website:** https://github.com/pravken/KeyFish
- **Support contact info:** https://github.com/pravken/KeyFish/issues
- **Product declarations:** no in-app purchases; the app does not depend on non-Microsoft drivers or NT services; it may be installed to alternate drives.
- **System requirements:** Keyboard (required). Memory: 2 GB minimum.

## Pricing and availability

- **Price:** Free
- **Markets:** all

## Age ratings (IARC questionnaire)

- **Category:** Utility, Productivity, Communication or Other.
- **Answers:** No to everything. The app has no violence, sexual content, gambling, user-to-user communication or content sharing, location sharing, digital purchases or unrestricted web access.
- **Expected rating:** 3+ / Everyone.

## Submission options: restricted capability

**runFullTrust**

> KeyFish is a classic Win32 desktop app (.NET Framework 4.8, WinForms) packaged with the Desktop Bridge. Its purpose is to fix text typed in the wrong keyboard layout in any app the user types in. For that it uses a low-level keyboard hook (SetWindowsHookEx WH_KEYBOARD_LL) to see the word being typed, SendInput to retype it in the right layout, the clipboard for commands on selected text, and UI Automation to detect password fields, where it never corrects anything. None of this is possible from a sandboxed app, so the package runs with full trust. Typed text is never stored or sent anywhere; see the privacy policy. Source code: https://github.com/pravken/KeyFish

## Notes for certification

> How to test:
>
> 1. Make sure two keyboard layouts are installed, for example English (United States) and Russian. KeyFish uses the layouts installed in Windows.
> 2. Start KeyFish. A fish icon appears in the notification area; double-click it to open Settings.
> 3. In Notepad, with the English layout on, type `ghbdtn vbh` followed by a space. KeyFish replaces it with «привет мир» (Russian for "hello world") and switches the layout to Russian.
> 4. With the Russian layout on, type `руддщ` followed by a space. KeyFish replaces it with "hello".
> 5. Press Pause right after a correction to undo it.
> 6. Select some text and press Ctrl+Shift+F2 to open the text actions menu.
>
> Task Manager shows two KeyFish.exe processes. The second one (`--password-guard`) is the password-field check, and it closes together with KeyFish.
>
> KeyFish goes online only to download optional extended dictionaries from its GitHub repository when the user turns them on in Settings. The Store version does not check for or install updates itself.

## Store listing: English (en-us)

**Description**

KeyFish fixes text typed in the wrong keyboard layout, for everyone who types in two alphabets.

Type `ghbdtn vbh` with the English layout on, and KeyFish turns it into «привет мир» and switches the layout for you. Type `руддщ` with the Russian layout on, and it becomes "hello". It works in any app: browsers, messengers, office apps, code editors.

KeyFish supports English, German, Polish, French, Spanish, Italian, Portuguese, Czech, Turkish and Serbian (Latin) against Russian, Ukrainian, Belarusian, Bulgarian, Macedonian, Serbian (Cyrillic), Greek, Hebrew and Georgian. It uses the layouts installed in Windows and decides by word lists and letter statistics of each language. Words it is not sure about are left alone.

Your typing stays on your computer. KeyFish keeps no log of what you type and sends nothing anywhere. In password fields it does not correct or remember anything.

KeyFish is free and open source.

**Product features**

1. Fixes words typed in the wrong layout as you type, often before the word is finished
2. Fixes accidental Caps Lock and two capital letters at the start of a word
3. Pause undoes a correction or switches the layout of the selected text
4. A menu of actions on the selected text: case, layout, transliteration, typos, quotes, line sorting, calculation
5. Paste without formatting with Ctrl+Shift+V
6. Text replacements: an abbreviation and a space expand into your text, with the date, the time or the clipboard if you like
7. A layout and a mode per app: KeyFish switches the layout when you go to the app, and can keep only the hotkeys or stay off there; rules by window title too
8. Remembers words you undo and learns from words you fix yourself
9. Never corrects in password fields; stays quiet in games and full-screen apps
10. Statistics of corrections, computed from counters only
11. Interface in English, Russian, Ukrainian, Belarusian, German, Polish, Serbian, French and Spanish
12. Light and dark themes, interface size from 100 to 200 %

**Search terms**

keyboard layout; wrong layout; layout switcher; auto switch layout; transliteration; text correction; typing

**What's new in this version**

First release in the Microsoft Store.

**Short description**

Fixes text typed in the wrong keyboard layout as you type, in any app. Private: nothing you type is stored or sent.

**Copyright and trademark info**

© 2026 Maxim Pravko. MIT License.

## Store listing: Russian (ru-ru)

**Описание**

KeyFish исправляет текст, набранный не в той раскладке, — для всех, кто печатает на двух алфавитах.

Наберите `ghbdtn vbh` в английской раскладке, и KeyFish превратит это в «привет мир» и сам переключит раскладку. Наберите `руддщ` в русской раскладке, и получится «hello». Программа работает в любых приложениях: браузерах, мессенджерах, офисных программах, редакторах кода.

KeyFish поддерживает английский, немецкий, польский, французский, испанский, итальянский, португальский, чешский, турецкий и сербский (латиница) в паре с русским, украинским, белорусским, болгарским, македонским, сербским (кириллица), греческим, ивритом и грузинским. Программа использует раскладки, установленные в Windows, и принимает решение по словарям и статистике букв каждого языка. Слова, в которых она не уверена, остаются без изменений.

Набранный текст остаётся на вашем компьютере. KeyFish не записывает то, что вы печатаете, и ничего никуда не отправляет. В полях пароля программа ничего не исправляет и не запоминает.

KeyFish бесплатна, исходный код открыт.

**Возможности**

1. Исправляет слова, набранные не в той раскладке, прямо при наборе, часто ещё до конца слова
2. Исправляет случайно включённый Caps Lock и две заглавные буквы в начале слова
3. Pause отменяет исправление или меняет раскладку выделенного текста
4. Меню действий с выделенным текстом: регистр, раскладка, транслитерация, опечатки, кавычки, сортировка строк, вычисление
5. Вставка без форматирования по Ctrl+Shift+V
6. Автозамены: сокращение и пробел превращаются в ваш текст, по желанию с датой, временем или содержимым буфера обмена
7. Раскладка и режим для каждой программы: KeyFish переключает раскладку при переходе в программу, а где нужно, оставляет только горячие клавиши или не работает; правила и по заголовку окна
8. Запоминает отменённые слова и учится на словах, которые вы исправили сами
9. Никогда не исправляет в полях пароля, не мешает в играх и полноэкранных программах
10. Статистика исправлений, только по счётчикам
11. Интерфейс на русском, английском, украинском, белорусском, немецком, польском, сербском, французском и испанском
12. Светлая и тёмная темы, размер интерфейса от 100 до 200 %

**Ключевые слова**

раскладка; не та раскладка; переключатель раскладки; автопереключение раскладки; транслитерация; исправление текста; набор текста

**Что нового в этой версии**

Первый выпуск в Microsoft Store.

**Краткое описание**

Исправляет текст, набранный не в той раскладке, прямо при наборе в любой программе. Набранное не сохраняется и не передаётся.

**Авторские права**

© 2026 Maxim Pravko. Лицензия MIT.
