# KeyFish privacy policy

*Last updated: November 10, 2026. [Русская версия ниже](#политика-конфиденциальности-keyfish).*

KeyFish fixes text typed in the wrong keyboard layout. To do that it has to see your keystrokes. This policy describes what it does with them and with anything else it handles. It applies to KeyFish from the Microsoft Store and to KeyFish downloaded from GitHub.

## What KeyFish does not do

- It does not collect, sell or share personal data. It has no accounts, no analytics, no advertising and no telemetry.
- It does not log what you type and never sends typed text anywhere.

## Typing

Only the word you are typing and the last few words are kept, in memory, while you type. They are dropped when you click, move the cursor or switch windows.

In password fields KeyFish does not correct or remember anything. A separate KeyFish process asks Windows whether the focused field is a password field, and it reads nothing else.

## What is stored on your computer

Everything below stays on your computer, and you can see and edit it in KeyFish's settings:

- your replacements, the apps excluded from correction and the layouts chosen per app;
- the "Never correct" and "Always correct" word lists: single words added when you undo a correction or fix a word yourself, never sentences or the text around them;
- the statistics counters: the number of corrections in total and per day for the last 62 days. They hold numbers only, with no words, apps or times;
- copies of the previous settings in the `backups` folder (the last 10);
- `crash.log`, with errors and crash records only.

The Store version keeps its files in its own app data folder (`%LOCALAPPDATA%\Packages\MaximPravko.KeyFish_…\LocalState`). The GitHub version keeps them next to `KeyFish.exe` or in `%APPDATA%\KeyFish`. Settings are in the registry under `HKCU\Software\KeyFish`.

Uninstalling the Store version removes its data folder.

## Network

KeyFish goes online only in these cases. None of them send anything about you or your typing:

- **Extended dictionaries.** If you turn them on in Settings, KeyFish downloads word lists from the releases of the KeyFish repository on GitHub and checks them against published checksums.
- **Updates (GitHub version only).** Once a day KeyFish asks GitHub for the latest release of its repository. The Store version is updated by the Microsoft Store.
- **Crash reports.** Nothing is sent automatically. If KeyFish crashed, it offers to open a GitHub issue with the error for you to read and submit yourself, or to copy it to the clipboard.

These requests go to GitHub, and GitHub's own privacy statement applies to them.

## Clipboard

Commands that work on selected text go through the clipboard. KeyFish puts back what was there and keeps its own text out of clipboard history and cloud sync.

## Contact

Questions about this policy: [github.com/pravken/KeyFish/issues](https://github.com/pravken/KeyFish/issues).

---

# Политика конфиденциальности KeyFish

*Обновлено 10 ноября 2026 г.*

KeyFish исправляет текст, набранный не в той раскладке. Для этого программе нужно видеть нажатия клавиш. Эта политика описывает, что KeyFish делает с ними и с другими данными, которые обрабатывает. Она относится и к версии из Microsoft Store, и к версии с GitHub.

## Чего KeyFish не делает

- Не собирает, не продаёт и не передаёт персональные данные. В программе нет учётных записей, аналитики, рекламы и телеметрии.
- Не записывает набранный текст и никуда его не отправляет.

## Набор текста

Пока вы печатаете, в памяти хранятся только набираемое слово и несколько предыдущих. Они удаляются при щелчке мышью, перемещении курсора или переключении окна.

В полях пароля KeyFish ничего не исправляет и не запоминает. Отдельный процесс KeyFish спрашивает у Windows, является ли поле в фокусе полем пароля, и больше ничего не считывает.

## Что хранится на компьютере

Всё перечисленное хранится только на вашем компьютере, и это можно посмотреть и изменить в настройках KeyFish:

- автозамены, программы без исправления и раскладки для программ;
- списки «Не исправлять» и «Всегда исправлять». В них попадают отдельные слова, когда вы отменяете исправление или исправляете слово сами, и никогда не попадают предложения или текст вокруг;
- счётчики статистики: число исправлений всего и по дням за последние 62 дня. Только числа, без слов, программ и времени;
- копии прежних настроек в папке `backups` (последние 10);
- `crash.log`, где записаны только ошибки и сведения о сбоях.

Версия из Store хранит файлы в собственной папке данных приложения (`%LOCALAPPDATA%\Packages\MaximPravko.KeyFish_…\LocalState`). Версия с GitHub хранит их рядом с `KeyFish.exe` или в `%APPDATA%\KeyFish`. Настройки хранятся в реестре в разделе `HKCU\Software\KeyFish`.

При удалении версии из Store её папка данных удаляется.

## Сеть

KeyFish обращается к сети только в следующих случаях. Ни в одном из них не передаются сведения о вас или о набранном тексте:

- **Расширенные словари.** Если вы включили их в настройках, KeyFish скачивает списки слов из релизов репозитория KeyFish на GitHub и сверяет их с опубликованными контрольными суммами.
- **Обновления (только версия с GitHub).** Раз в сутки KeyFish запрашивает у GitHub последний релиз своего репозитория. Версию из Store обновляет Microsoft Store.
- **Отчёты о сбоях.** Автоматически ничего не отправляется. После сбоя KeyFish предлагает открыть на GitHub обращение с описанием ошибки, которое вы сами просматриваете и отправляете, или скопировать его в буфер обмена.

Эти запросы обрабатывает GitHub, и к ним применяется его собственная политика конфиденциальности.

## Буфер обмена

Команды для выделенного текста работают через буфер обмена. KeyFish возвращает его прежнее содержимое и не допускает свой текст в журнал буфера обмена и облачную синхронизацию.

## Контакты

Вопросы об этой политике: [github.com/pravken/KeyFish/issues](https://github.com/pravken/KeyFish/issues).
