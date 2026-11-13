# KeyFish in the Microsoft Store: submission texts

What goes into Partner Center for each submission. Product: KeyFish, Store ID 9P8BDBQM7G22.

## Properties

- **Category:** Utilities & tools
- **Privacy policy URL:** https://github.com/pravken/KeyFish/blob/main/PRIVACY.md
- **Website:** https://github.com/pravken/KeyFish
- **Support contact info:** https://github.com/pravken/KeyFish/issues
- **Product declarations:** no in-app purchases; may be installed to alternate drives; data may be included in OneDrive backups; record and broadcast off (games only).
- **Device families:** Windows 10/11 Desktop only; "Let Microsoft decide future device families" off.
- **Listing languages:** all nine interface languages: en-us, ru-ru, uk-ua, be-by, de-de, pl-pl, sr-cyrl-rs, fr-fr, es-es. Screenshots: `obj\store\{1..5}-<lang>.png`, made by `msix\store-screenshots.ps1` with the captions below.
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

The field takes at most 500 characters.

> Win32 desktop app (.NET 4.8 WinForms) packaged with the Desktop Bridge. It fixes text typed in the wrong keyboard layout in any app: a low-level keyboard hook (WH_KEYBOARD_LL) sees the word being typed, SendInput retypes it, the clipboard serves commands on selected text, and UI Automation detects password fields, where nothing is corrected. This is impossible in a sandbox. Typed text is never stored or sent. Source: https://github.com/pravken/KeyFish

## Notes for certification

> How to test:
>
> 1. Make sure two keyboard layouts are installed, for example English (United States) and Russian. KeyFish uses the layouts installed in Windows.
> 2. Start KeyFish. A fish icon appears in the notification area; double-click it to open Settings. If the icon is not visible, it is in the hidden icons area: click the ^ arrow on the taskbar.
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

Your typing stays on your computer. KeyFish keeps no log of what you type and never sends it anywhere. In password fields it does not correct or remember anything.

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

• Greek typed without accents is recognized
• Hints for uncertain words (off by default)
• Short words and whole phrases are fixed together
• Better handling of selections, erasing and Windows 11 Notepad
• Updated icon and bug fixes

**Short description**

Fixes text typed in the wrong keyboard layout as you type, in any app. Private: nothing you type is stored or sent.

**Copyright and trademark info**

© 2026 Maxim Pravko. MIT License.

**Screenshot captions**

1. Fixes the keyboard layout as you type / руддщ becomes hello, ghbdtn becomes привет — in any app
2. Actions on the selected text / Case, layout, transliteration, typos and more — Ctrl+Shift+F2
3. Statistics without a typing log / Only counters are stored. Nothing you type is recorded or sent
4. A layout and a mode for each app / KeyFish switches the layout when you go to the app. Where you need, it keeps only the hotkeys or stays off
5. Light and dark themes / Your own colors for the window and the tray icon

## Store listing: Russian (ru-ru)

**Описание**

KeyFish исправляет текст, набранный не в той раскладке, — для всех, кто печатает на двух алфавитах.

Наберите `ghbdtn vbh` в английской раскладке, и KeyFish превратит это в «привет мир» и сам переключит раскладку. Наберите `руддщ` в русской раскладке, и получится «hello». Программа работает в любых приложениях: браузерах, мессенджерах, офисных программах, редакторах кода.

KeyFish поддерживает английский, немецкий, польский, французский, испанский, итальянский, португальский, чешский, турецкий и сербский (латиница) в паре с русским, украинским, белорусским, болгарским, македонским, сербским (кириллица), греческим, ивритом и грузинским. Программа использует раскладки, установленные в Windows, и принимает решение по словарям и статистике букв каждого языка. Слова, в которых она не уверена, остаются без изменений.

Набранный текст остаётся на вашем компьютере. KeyFish не записывает то, что вы печатаете, и никуда это не отправляет. В полях пароля программа ничего не исправляет и не запоминает.

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

• Распознаётся греческий текст без ударений
• Подсказки для неочевидных слов (по умолчанию выключены)
• Короткие слова и целые фразы исправляются вместе
• Точнее работа с выделением, стиранием и Блокнотом Windows 11
• Обновлённая иконка и исправления ошибок

**Краткое описание**

Исправляет текст, набранный не в той раскладке, прямо при наборе в любой программе. Набранное не сохраняется и не передаётся.

**Авторские права**

© 2026 Maxim Pravko. Лицензия MIT.

**Подписи к скриншотам**

1. Исправляет раскладку прямо при наборе / ghbdtn превращается в «привет», руддщ — в hello, в любой программе
2. Действия с выделенным текстом / Регистр, раскладка, транслитерация, опечатки и другое — Ctrl+Shift+F2
3. Статистика без журнала набора / Хранятся только счётчики. Набранный текст не записывается и не передаётся
4. Раскладка и режим для каждой программы / KeyFish переключает раскладку при переходе в программу. Где нужно, оставляет только горячие клавиши или не работает
5. Светлая и тёмная темы / Собственные цвета для окна и значка в области уведомлений

## Store listing: Ukrainian (uk-ua)

**Description**

KeyFish виправляє текст, набраний не в тій розкладці, — для всіх, хто друкує двома абетками.

Наберіть ghbdsn в англійській розкладці, і KeyFish перетворить це на «привіт» і сам перемкне розкладку. Наберіть руддщ в українській розкладці, і вийде «hello». Програма працює в будь-яких застосунках: браузерах, месенджерах, офісних програмах, редакторах коду.

KeyFish підтримує англійську, німецьку, польську, французьку, іспанську, італійську, португальську, чеську, турецьку та сербську (латиниця) в парі з російською, українською, білоруською, болгарською, македонською, сербською (кирилиця), грецькою, івритом і грузинською. Програма використовує розкладки, встановлені у Windows, і ухвалює рішення за словниками та статистикою літер кожної мови. Слова, щодо яких вона не впевнена, залишаються без змін.

Набраний текст залишається на вашому комп’ютері. KeyFish не записує те, що ви друкуєте, і нікуди це не надсилає. У полях пароля програма нічого не виправляє і не запам’ятовує.

KeyFish безкоштовна, вихідний код відкритий.

**Product features**

1. Виправляє слова, набрані не в тій розкладці, просто під час набору, часто ще до кінця слова
2. Виправляє випадково ввімкнений Caps Lock і дві великі літери на початку слова
3. Pause скасовує виправлення або змінює розкладку виділеного тексту
4. Меню дій з виділеним текстом: регістр, розкладка, транслітерація, одруківки, лапки, сортування рядків, обчислення
5. Вставлення без форматування за Ctrl+Shift+V
6. Автозаміни: скорочення і пробіл перетворюються на ваш текст, за бажанням з датою, часом або вмістом буфера обміну
7. Розкладка й режим для кожної програми: KeyFish перемикає розкладку під час переходу до програми, а де потрібно, залишає лише гарячі клавіші або не працює; правила й за заголовком вікна
8. Запам’ятовує скасовані слова й навчається на словах, які ви виправили самі
9. Ніколи не виправляє в полях пароля, не заважає в іграх і повноекранних програмах
10. Статистика виправлень, лише за лічильниками
11. Інтерфейс українською, англійською, російською, білоруською, німецькою, польською, сербською, французькою та іспанською
12. Світла й темна теми, розмір інтерфейсу від 100 до 200 %

**Search terms**

розкладка клавіатури; не та розкладка; перемикач розкладки; автоперемикання розкладки; транслітерація; виправлення тексту; набір тексту

**What's new in this version**

• Розпізнається грецький текст без наголосів
• Підказки для неочевидних слів (типово вимкнені)
• Короткі слова й цілі фрази виправляються разом
• Точніша робота з виділенням, стиранням і Блокнотом Windows 11
• Оновлена іконка та виправлення помилок

**Short description**

Виправляє текст, набраний не в тій розкладці, просто під час набору в будь-якій програмі. Набране не зберігається і не передається.

**Copyright and trademark info**

© 2026 Maxim Pravko. Ліцензія MIT.

**Screenshot captions**

1. Виправляє розкладку просто під час набору / ghbdsn перетворюється на «привіт», руддщ — на hello, у будь-якій програмі
2. Дії з виділеним текстом / Регістр, розкладка, транслітерація, одруківки та інше — Ctrl+Shift+F2
3. Статистика без журналу набору / Зберігаються лише лічильники. Набраний текст не записується і не передається
4. Розкладка й режим для кожної програми / KeyFish перемикає розкладку під час переходу до програми. Де потрібно, залишає лише гарячі клавіші або не працює
5. Світла й темна теми / Власні кольори для вікна та значка в області сповіщень

## Store listing: Belarusian (be-by)

**Description**

KeyFish выпраўляе тэкст, набраны не ў той раскладцы, — для ўсіх, хто друкуе на дзвюх абетках.

Набярыце ghsdbnfyyt у англійскай раскладцы, і KeyFish ператворыць гэта ў «прывітанне» і сам пераключыць раскладку. Набярыце руддщ у рускай раскладцы, і атрымаецца «hello». Праграма працуе ў любых дадатках: браўзерах, месенджарах, офісных праграмах, рэдактарах кода.

KeyFish падтрымлівае англійскую, нямецкую, польскую, французскую, іспанскую, італьянскую, партугальскую, чэшскую, турэцкую і сербскую (лацініца) у пары з рускай, украінскай, беларускай, балгарскай, македонскай, сербскай (кірыліца), грэчаскай, іўрытам і грузінскай. Праграма выкарыстоўвае раскладкі, усталяваныя ў Windows, і прымае рашэнне па слоўніках і статыстыцы літар кожнай мовы. Словы, у якіх яна не ўпэўнена, застаюцца без змен.

Набраны тэкст застаецца на вашым камп’ютары. KeyFish не запісвае тое, што вы друкуеце, і нікуды гэта не адпраўляе. У палях пароля праграма нічога не выпраўляе і не запамінае.

KeyFish бясплатная, зыходны код адкрыты.

**Product features**

1. Выпраўляе словы, набраныя не ў той раскладцы, проста падчас набору, часта яшчэ да канца слова
2. Выпраўляе выпадкова ўключаны Caps Lock і дзве вялікія літары ў пачатку слова
3. Pause адмяняе выпраўленне або мяняе раскладку вылучанага тэксту
4. Меню дзеянняў з вылучаным тэкстам: рэгістр, раскладка, транслітарацыя, памылкі друку, двукоссі, сартаванне радкоў, вылічэнне
5. Устаўка без фарматавання па Ctrl+Shift+V
6. Аўтазамены: скарачэнне і прабел ператвараюцца ў ваш тэкст, па жаданні з датай, часам або змесцівам буфера абмену
7. Раскладка і рэжым для кожнай праграмы: KeyFish пераключае раскладку пры пераходзе ў праграму, а дзе трэба, пакідае толькі гарачыя клавішы або не працуе; правілы і па загалоўку акна
8. Запамінае адмененыя словы і вучыцца на словах, якія вы выправілі самі
9. Ніколі не выпраўляе ў палях пароля, не замінае ў гульнях і поўнаэкранных праграмах
10. Статыстыка выпраўленняў, толькі па лічыльніках
11. Інтэрфейс на беларускай, англійскай, рускай, украінскай, нямецкай, польскай, сербскай, французскай і іспанскай
12. Светлая і цёмная тэмы, памер інтэрфейсу ад 100 да 200 %

**Search terms**

раскладка клавіятуры; не тая раскладка; пераключальнік раскладкі; аўтапераключэнне раскладкі; транслітарацыя; выпраўленне тэксту; набор тэксту

**What's new in this version**

• Распазнаецца грэчаскі тэкст без націскаў
• Падказкі для неадназначных слоў (па змаўчанні выключаныя)
• Кароткія словы і цэлыя фразы выпраўляюцца разам
• Дакладнейшая праца з вылучэннем, сціраннем і Нататнікам Windows 11
• Абноўлены значок і выпраўленні памылак

**Short description**

Выпраўляе тэкст, набраны не ў той раскладцы, проста падчас набору ў любой праграме. Набранае не захоўваецца і не перадаецца.

**Copyright and trademark info**

© 2026 Maxim Pravko. Ліцэнзія MIT.

**Screenshot captions**

1. Выпраўляе раскладку проста падчас набору / ghsdbnfyyt ператвараецца ў «прывітанне», руддщ — у hello, у любой праграме
2. Дзеянні з вылучаным тэкстам / Рэгістр, раскладка, транслітарацыя, памылкі друку і іншае — Ctrl+Shift+F2
3. Статыстыка без журнала набору / Захоўваюцца толькі лічыльнікі. Набраны тэкст не запісваецца і не перадаецца
4. Раскладка і рэжым для кожнай праграмы / KeyFish пераключае раскладку пры пераходзе ў праграму. Дзе трэба, пакідае толькі гарачыя клавішы або не працуе
5. Светлая і цёмная тэмы / Уласныя колеры для акна і значка ў вобласці апавяшчэнняў

## Store listing: German (de-de)

**Description**

KeyFish korrigiert Text, der im falschen Tastaturlayout getippt wurde – für alle, die mit zwei Alphabeten schreiben.

Tippen Sie bei aktivem englischem Layout ghbdtn vbh, und KeyFish macht daraus „привет мир“ und wechselt das Layout selbst. Tippen Sie bei aktivem russischem Layout руддщ, und es wird zu „hello“. KeyFish funktioniert in jeder Anwendung: in Browsern, Messengern, Office-Programmen und Code-Editoren.

KeyFish unterstützt Englisch, Deutsch, Polnisch, Französisch, Spanisch, Italienisch, Portugiesisch, Tschechisch, Türkisch und Serbisch (lateinisch) in Kombination mit Russisch, Ukrainisch, Belarussisch, Bulgarisch, Mazedonisch, Serbisch (kyrillisch), Griechisch, Hebräisch und Georgisch. Es verwendet die in Windows installierten Layouts und entscheidet anhand von Wortlisten und der Buchstabenstatistik jeder Sprache. Wörter, bei denen es sich nicht sicher ist, bleiben unverändert.

Was Sie tippen, bleibt auf Ihrem Computer. KeyFish protokolliert Ihre Eingaben nicht und sendet sie nirgendwohin. In Kennwortfeldern korrigiert und speichert es nichts.

KeyFish ist kostenlos und Open Source.

**Product features**

1. Korrigiert im falschen Layout getippte Wörter beim Tippen, oft schon bevor das Wort zu Ende ist
2. Korrigiert versehentlich aktiviertes Caps Lock und zwei Großbuchstaben am Wortanfang
3. Pause macht eine Korrektur rückgängig oder wechselt das Layout des markierten Textes
4. Ein Menü mit Aktionen für markierten Text: Groß-/Kleinschreibung, Layout, Transliteration, Tippfehler, Anführungszeichen, Zeilensortierung, Berechnung
5. Einfügen ohne Formatierung mit Ctrl+Shift+V
6. Textersetzungen: Ein Kürzel mit Leerzeichen wird zu Ihrem Text, auf Wunsch mit Datum, Uhrzeit oder dem Inhalt der Zwischenablage
7. Layout und Modus pro App: KeyFish wechselt das Layout, wenn Sie zur App wechseln, und kann dort nur die Tastenkürzel aktiv lassen oder pausieren; Regeln auch nach Fenstertitel
8. Merkt sich rückgängig gemachte Wörter und lernt aus Wörtern, die Sie selbst korrigieren
9. Korrigiert nie in Kennwortfeldern; stört nicht in Spielen und Vollbildanwendungen
10. Korrekturstatistik, nur aus Zählern berechnet
11. Oberfläche auf Deutsch, Englisch, Russisch, Ukrainisch, Belarussisch, Polnisch, Serbisch, Französisch und Spanisch
12. Helles und dunkles Design, Oberflächengröße von 100 bis 200 %

**Search terms**

Tastaturlayout; falsches Layout; Layout wechseln; automatischer Layoutwechsel; Transliteration; Textkorrektur; kyrillische Tastatur

**What's new in this version**

• Griechisch ohne Akzente wird erkannt
• Vorschläge für unsichere Wörter (standardmäßig aus)
• Kurze Wörter und ganze Sätze werden gemeinsam korrigiert
• Zuverlässiger bei Markierungen, beim Löschen und im Editor von Windows 11
• Überarbeitetes Symbol und Fehlerbehebungen

**Short description**

Korrigiert Text, der im falschen Tastaturlayout getippt wurde, direkt beim Tippen in jeder Anwendung. Privat: Nichts, was Sie tippen, wird gespeichert oder gesendet.

**Copyright and trademark info**

© 2026 Maxim Pravko. MIT-Lizenz.

**Screenshot captions**

1. Korrigiert das Tastaturlayout beim Tippen / руддщ wird zu hello, ghbdtn zu привет – in jeder Anwendung
2. Aktionen für markierten Text / Groß-/Kleinschreibung, Layout, Transliteration, Tippfehler und mehr – Ctrl+Shift+F2
3. Statistik ohne Eingabeprotokoll / Nur Zähler werden gespeichert. Getippter Text wird weder aufgezeichnet noch gesendet
4. Layout und Modus für jede App / KeyFish wechselt das Layout, wenn Sie zur App wechseln. Wo nötig, bleiben nur die Tastenkürzel aktiv oder es pausiert
5. Helles und dunkles Design / Eigene Farben für das Fenster und das Symbol im Infobereich

## Store listing: Polish (pl-pl)

**Description**

KeyFish poprawia tekst wpisany w niewłaściwym układzie klawiatury – dla wszystkich, którzy piszą w dwóch alfabetach.

Po wpisaniu ghbdtn vbh przy włączonym układzie angielskim KeyFish zamienia ten tekst na „привет мир” i sam przełącza układ. Po wpisaniu руддщ przy włączonym układzie rosyjskim powstaje „hello”. Program działa w każdej aplikacji: przeglądarkach, komunikatorach, programach biurowych i edytorach kodu.

KeyFish obsługuje angielski, niemiecki, polski, francuski, hiszpański, włoski, portugalski, czeski, turecki i serbski (łacinka) w parze z rosyjskim, ukraińskim, białoruskim, bułgarskim, macedońskim, serbskim (cyrylica), greckim, hebrajskim i gruzińskim. Korzysta z układów zainstalowanych w systemie Windows i decyduje na podstawie list słów i statystyki liter każdego języka. Słowa, co do których nie ma pewności, pozostają bez zmian.

Wpisywany tekst pozostaje na komputerze. KeyFish go nie zapisuje i nigdzie nie wysyła. W polach haseł niczego nie poprawia ani nie zapamiętuje.

KeyFish jest bezpłatny i ma otwarty kod źródłowy.

**Product features**

1. Poprawia słowa wpisane w niewłaściwym układzie już podczas pisania, często przed końcem słowa
2. Poprawia przypadkowo włączony Caps Lock i dwie wielkie litery na początku słowa
3. Pause cofa poprawkę lub zmienia układ zaznaczonego tekstu
4. Menu działań na zaznaczonym tekście: wielkość liter, układ, transliteracja, literówki, cudzysłowy, sortowanie wierszy, obliczenia
5. Wklejanie bez formatowania skrótem Ctrl+Shift+V
6. Zamienniki tekstu: skrót i spacja zamieniają się w wybrany tekst, opcjonalnie z datą, godziną lub zawartością schowka
7. Układ i tryb dla każdego programu: KeyFish przełącza układ po przejściu do programu, a w razie potrzeby zostawia tam tylko skróty klawiszowe lub się wyłącza; reguły także według tytułu okna
8. Zapamiętuje słowa, których poprawkę cofnięto, i uczy się na słowach poprawionych ręcznie
9. Nigdy nie poprawia w polach haseł; nie przeszkadza w grach i programach pełnoekranowych
10. Statystyki poprawek, liczone wyłącznie z liczników
11. Interfejs po polsku, angielsku, rosyjsku, ukraińsku, białorusku, niemiecku, serbsku, francusku i hiszpańsku
12. Jasny i ciemny motyw, rozmiar interfejsu od 100 do 200 %

**Search terms**

układ klawiatury; zły układ klawiatury; przełącznik układu; automatyczna zmiana układu; transliteracja; poprawianie tekstu; cyrylica

**What's new in this version**

• Rozpoznawanie greckiego tekstu bez akcentów
• Podpowiedzi dla niepewnych słów (domyślnie wyłączone)
• Krótkie słowa i całe frazy są poprawiane razem
• Lepsza obsługa zaznaczania, usuwania i Notatnika w Windows 11
• Odświeżona ikona i poprawki błędów

**Short description**

Poprawia tekst wpisany w niewłaściwym układzie klawiatury już podczas pisania, w każdym programie. Prywatność: wpisywany tekst nie jest zapisywany ani wysyłany.

**Copyright and trademark info**

© 2026 Maxim Pravko. Licencja MIT.

**Screenshot captions**

1. Poprawia układ klawiatury podczas pisania / руддщ zmienia się w hello, ghbdtn w привет – w każdym programie
2. Działania na zaznaczonym tekście / Wielkość liter, układ, transliteracja, literówki i więcej – Ctrl+Shift+F2
3. Statystyki bez zapisu wpisywanego tekstu / Przechowywane są tylko liczniki. Wpisywany tekst nie jest zapisywany ani wysyłany
4. Układ i tryb dla każdego programu / KeyFish przełącza układ po przejściu do programu. W razie potrzeby zostawia tylko skróty klawiszowe lub się wyłącza
5. Jasny i ciemny motyw / Własne kolory okna i ikony w obszarze powiadomień

## Store listing: Serbian, Cyrillic (sr-cyrl-rs)

**Description**

KeyFish исправља текст откуцан у погрешном распореду тастатуре — за све који куцају на два писма.

Откуцајте ghbdtn vbh док је укључен енглески распоред и KeyFish ће то претворити у „привет мир“ и сам променити распоред. Откуцајте руддщ док је укључен руски распоред и добићете „hello“. Програм ради у свим апликацијама: прегледачима, апликацијама за дописивање, канцеларијским програмима и уређивачима кода.

KeyFish подржава енглески, немачки, пољски, француски, шпански, италијански, португалски, чешки, турски и српски (латиница) у пару са руским, украјинским, белоруским, бугарским, македонским, српским (ћирилица), грчким, хебрејским и грузијским. Користи распореде инсталиране у Windows-у и одлучује према списковима речи и статистици слова сваког језика. Речи у које није сигуран остају непромењене.

Оно што куцате остаје на вашем рачунару. KeyFish не бележи шта куцате и то нигде не шаље. У пољима за лозинку ништа не исправља и не памти.

KeyFish је бесплатан и отвореног кода.

**Product features**

1. Исправља речи откуцане у погрешном распореду још док куцате, често пре краја речи
2. Исправља случајно укључен Caps Lock и два велика слова на почетку речи
3. Pause поништава исправку или мења распоред означеног текста
4. Мени радњи над означеним текстом: величина слова, распоред, транслитерација, грешке у куцању, наводници, сортирање редова, рачунање
5. Налепљивање без форматирања помоћу Ctrl+Shift+V
6. Замене текста: скраћеница и размак претварају се у ваш текст, по жељи са датумом, временом или садржајем оставе
7. Распоред и режим за сваки програм: KeyFish мења распоред када пређете у програм, а где је потребно, оставља само пречице или не ради; правила и према наслову прозора
8. Памти речи чију сте исправку поништили и учи на речима које сами исправите
9. Никада не исправља у пољима за лозинку; не смета у играма и програмима преко целог екрана
10. Статистика исправки, само на основу бројача
11. Интерфејс на српском, енглеском, руском, украјинском, белоруском, немачком, пољском, француском и шпанском
12. Светла и тамна тема, величина интерфејса од 100 до 200 %

**Search terms**

распоред тастатуре; погрешан распоред; промена распореда; аутоматска промена распореда; транслитерација; исправљање текста; ћирилица

**What's new in this version**

• Препознаје се грчки текст без акцената
• Предлози за несигурне речи (подразумевано искључени)
• Кратке речи и целе фразе исправљају се заједно
• Боље понашање при означавању, брисању и у Бележници у Windows 11
• Освежена иконица и исправке грешака

**Short description**

Исправља текст откуцан у погрешном распореду тастатуре још док куцате, у сваком програму. Приватно: ништа од откуцаног се не чува нити шаље.

**Copyright and trademark info**

© 2026 Maxim Pravko. Лиценца MIT.

**Screenshot captions**

1. Исправља распоред тастатуре док куцате / руддщ постаје hello, ghbdtn постаје привет — у сваком програму
2. Радње над означеним текстом / Величина слова, распоред, транслитерација, грешке у куцању и друго — Ctrl+Shift+F2
3. Статистика без записа куцања / Чувају се само бројачи. Откуцани текст се не бележи нити шаље
4. Распоред и режим за сваки програм / KeyFish мења распоред када пређете у програм. Где је потребно, оставља само пречице или не ради
5. Светла и тамна тема / Сопствене боје прозора и иконе у системској палети

## Store listing: French (fr-fr)

**Description**

KeyFish corrige le texte saisi dans la mauvaise disposition de clavier, pour tous ceux qui écrivent avec deux alphabets.

Tapez ghbdtn vbh avec la disposition anglaise active : KeyFish le transforme en « привет мир » et change la disposition pour vous. Tapez руддщ avec la disposition russe active : vous obtenez « hello ». KeyFish fonctionne dans toutes les applications : navigateurs, messageries, suites bureautiques, éditeurs de code.

KeyFish prend en charge l'anglais, l'allemand, le polonais, le français, l'espagnol, l'italien, le portugais, le tchèque, le turc et le serbe (latin) face au russe, à l'ukrainien, au biélorusse, au bulgare, au macédonien, au serbe (cyrillique), au grec, à l'hébreu et au géorgien. Il utilise les dispositions installées dans Windows et décide d'après les listes de mots et les statistiques de lettres de chaque langue. Les mots dont il n'est pas sûr restent tels quels.

Ce que vous tapez reste sur votre ordinateur. KeyFish n'enregistre pas votre saisie et ne l'envoie nulle part. Dans les champs de mot de passe, il ne corrige et ne mémorise rien.

KeyFish est gratuit et open source.

**Product features**

1. Corrige les mots tapés dans la mauvaise disposition pendant la saisie, souvent avant la fin du mot
2. Corrige le Verr. Maj activé par erreur et deux majuscules en début de mot
3. Pause annule une correction ou change la disposition du texte sélectionné
4. Un menu d'actions sur le texte sélectionné : casse, disposition, translittération, fautes de frappe, guillemets, tri des lignes, calcul
5. Collage sans mise en forme avec Ctrl+Shift+V
6. Remplacements de texte : une abréviation suivie d'une espace devient votre texte, avec la date, l'heure ou le contenu du presse-papiers si vous le souhaitez
7. Une disposition et un mode par application : KeyFish change de disposition quand vous y passez, et peut n'y garder que les raccourcis ou s'y désactiver ; règles aussi par titre de fenêtre
8. Retient les mots dont vous annulez la correction et apprend des mots que vous corrigez vous-même
9. Ne corrige jamais dans les champs de mot de passe ; reste discret dans les jeux et les applications en plein écran
10. Statistiques des corrections, calculées uniquement à partir de compteurs
11. Interface en français, anglais, russe, ukrainien, biélorusse, allemand, polonais, serbe et espagnol
12. Thèmes clair et sombre, taille de l'interface de 100 à 200 %

**Search terms**

disposition clavier; mauvaise disposition; changer de disposition; changement automatique de clavier; translittération; correction de texte; clavier cyrillique

**What's new in this version**

• Le grec saisi sans accents est reconnu
• Suggestions pour les mots incertains (désactivées par défaut)
• Les mots courts et les phrases entières sont corrigés ensemble
• Meilleure prise en charge des sélections, de l'effacement et du Bloc-notes de Windows 11
• Icône mise à jour et corrections de bugs

**Short description**

Corrige pendant la saisie le texte tapé dans la mauvaise disposition de clavier, dans toutes les applications. Confidentiel : rien de ce que vous tapez n'est enregistré ni envoyé.

**Copyright and trademark info**

© 2026 Maxim Pravko. Licence MIT.

**Screenshot captions**

1. Corrige la disposition du clavier pendant la saisie / руддщ devient hello, ghbdtn devient привет, dans toutes les applications
2. Actions sur le texte sélectionné / Casse, disposition, translittération, fautes de frappe et plus — Ctrl+Shift+F2
3. Statistiques sans journal de saisie / Seuls des compteurs sont conservés. Rien de ce que vous tapez n'est enregistré ni envoyé
4. Une disposition et un mode par application / KeyFish change de disposition quand vous passez à l'application. Si besoin, il n'y garde que les raccourcis ou s'y désactive
5. Thèmes clair et sombre / Vos propres couleurs pour la fenêtre et l'icône de la zone de notification

## Store listing: Spanish (es-es)

**Description**

KeyFish corrige el texto escrito con la distribución de teclado equivocada, para todos los que escriben en dos alfabetos.

Escriba ghbdtn vbh con la distribución inglesa activa y KeyFish lo convierte en «привет мир» y cambia la distribución por usted. Escriba руддщ con la distribución rusa activa y se convierte en «hello». Funciona en cualquier aplicación: navegadores, mensajería, aplicaciones de oficina y editores de código.

KeyFish admite inglés, alemán, polaco, francés, español, italiano, portugués, checo, turco y serbio (latino) frente a ruso, ucraniano, bielorruso, búlgaro, macedonio, serbio (cirílico), griego, hebreo y georgiano. Usa las distribuciones instaladas en Windows y decide según listas de palabras y estadísticas de letras de cada idioma. Las palabras de las que no está seguro se dejan como están.

Lo que escribe se queda en su equipo. KeyFish no registra lo que escribe ni lo envía a ningún sitio. En los campos de contraseña no corrige ni recuerda nada.

KeyFish es gratuito y de código abierto.

**Product features**

1. Corrige las palabras escritas con la distribución equivocada mientras escribe, a menudo antes de terminar la palabra
2. Corrige el Bloq Mayús activado por error y dos mayúsculas al principio de una palabra
3. Pausa deshace una corrección o cambia la distribución del texto seleccionado
4. Un menú de acciones sobre el texto seleccionado: mayúsculas y minúsculas, distribución, transliteración, erratas, comillas, ordenación de líneas, cálculo
5. Pegar sin formato con Ctrl+Shift+V
6. Reemplazos de texto: una abreviatura y un espacio se convierten en su texto, con la fecha, la hora o el contenido del portapapeles si lo desea
7. Una distribución y un modo por aplicación: KeyFish cambia la distribución al pasar a la aplicación y puede dejar allí solo los atajos o desactivarse; también reglas por título de ventana
8. Recuerda las palabras cuya corrección deshace y aprende de las palabras que corrige usted mismo
9. Nunca corrige en campos de contraseña; no molesta en juegos ni en aplicaciones a pantalla completa
10. Estadísticas de correcciones, calculadas solo a partir de contadores
11. Interfaz en español, inglés, ruso, ucraniano, bielorruso, alemán, polaco, serbio y francés
12. Temas claro y oscuro, tamaño de la interfaz del 100 al 200 %

**Search terms**

distribución de teclado; distribución incorrecta; cambiar distribución; cambio automático de teclado; transliteración; corrección de texto; teclado cirílico

**What's new in this version**

• Se reconoce el griego escrito sin acentos
• Sugerencias para palabras dudosas (desactivadas por defecto)
• Las palabras cortas y las frases enteras se corrigen juntas
• Mejor manejo de selecciones, borrado y el Bloc de notas de Windows 11
• Icono actualizado y correcciones de errores

**Short description**

Corrige mientras escribe el texto escrito con la distribución de teclado equivocada, en cualquier aplicación. Privado: nada de lo que escribe se guarda ni se envía.

**Copyright and trademark info**

© 2026 Maxim Pravko. Licencia MIT.

**Screenshot captions**

1. Corrige la distribución del teclado mientras escribe / руддщ se convierte en hello y ghbdtn en привет, en cualquier aplicación
2. Acciones sobre el texto seleccionado / Mayúsculas, distribución, transliteración, erratas y más — Ctrl+Shift+F2
3. Estadísticas sin registro de escritura / Solo se guardan contadores. Nada de lo que escribe se registra ni se envía
4. Una distribución y un modo por aplicación / KeyFish cambia la distribución al pasar a la aplicación. Donde haga falta, deja solo los atajos o se desactiva
5. Temas claro y oscuro / Colores propios para la ventana y el icono del área de notificación
