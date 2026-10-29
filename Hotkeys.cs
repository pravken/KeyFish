using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// App's hotkeys: registering them, and what they do with the selected text through the clipboard
partial class App
{
    // Hotkeys: id = index + 1, stored in the registry as a Keys value (key + modifiers), 0 means off
    public static string[] HotkeyTitles
    {
        get
        {
            return new[]
            {
                L.T("Смена раскладки выделенного текста; сразу после автоисправления — его отмена (как Ctrl+Z)",
                    "Switch the layout of selected text; right after an auto-correction, undo it (as Ctrl+Z does)"),
                L.T("Смена раскладки выделенного текста (дополнительное сочетание)", "Switch the layout of selected text (alternative hotkey)"),
                L.T("Смена регистра выделенного текста (aa → Aa → AA)", "Change case of selected text (aa → Aa → AA)"),
                L.T("Транслитерация выделенного текста (привет ⇄ privet)", "Transliterate selected text (привет ⇄ privet)"),
                L.T("Смена раскладки строки до курсора", "Switch the layout of the line before the cursor"),
                L.T("Смена раскладки текста в буфере обмена", "Switch the layout of the text in the clipboard"),
                L.T("Приостановить исправление на 1 час / возобновить", "Pause correction for 1 hour / resume"),
                L.T("Удаление форматирования выделенного текста: полужирный, курсив, ссылки, стилизованные символы",
                    "Clear the formatting of selected text: bold, italic, links, styled characters"),
                L.T("Меню действий с выделенным текстом: регистр, раскладка, пробелы, строки, вычисление выражения",
                    "Menu of actions on selected text: case, layout, spaces, lines, calculating an expression"),
                L.T("Вставка текста из буфера обмена без форматирования", "Paste text from the clipboard without formatting")
            };
        }
    }

    public static readonly Keys[] HotkeyDefaults =
    {
        Keys.Pause, Keys.Control | Keys.Shift | Keys.Space, Keys.Shift | Keys.F3, Keys.Shift | Keys.Pause, Keys.Alt | Keys.Pause,
        Keys.None,   // the clipboard: off until the user picks keys for it
        Keys.None,   // pause: the same
        Keys.Shift | Keys.F2, Keys.Control | Keys.Shift | Keys.F2, Keys.Control | Keys.Shift | Keys.V
    };

    const int HkLayout = 1, HkLayoutAlt = 2, HkCase = 3, HkTranslit = 4, HkLine = 5, HkClipboard = 6, HkPause = 7, HkPlain = 8, HkActions = 9, HkPastePlain = 10;

    static Keys GetHotkey(int i) { return (Keys)GetSetting("Hotkey" + (i + 1), (int)HotkeyDefaults[i]); }

    // Registers all hotkeys again; returns the ones taken by other apps
    List<string> RegisterHotkeys()
    {
        hotkeys.UnregisterAll();
        var failed = new List<string>();
        notAllowed.Clear();
        for (int i = 0; i < HotkeyDefaults.Length; i++)
        {
            Keys k = GetHotkey(i);
            if (k == Keys.None) continue;
            if (!HotkeyBox.Allowed(k)) { notAllowed.Add(SettingsForm.Format(k)); continue; }   // Enter or a letter stored by an older version or by hand
            if (!hotkeys.Register(i + 1, Modifiers(k), k & Keys.KeyCode)) failed.Add(SettingsForm.Format(k));
        }
        return failed;
    }

    static uint Modifiers(Keys k)
    {
        return ((k & Keys.Control) != 0 ? Native.MOD_CONTROL : 0) | ((k & Keys.Shift) != 0 ? Native.MOD_SHIFT : 0) |
               ((k & Keys.Alt) != 0 ? Native.MOD_ALT : 0);
    }

    // Hotkeys stored that can't be used as one (Enter, a plain letter): left off, and said once, with the others
    readonly List<string> notAllowed = new List<string>();

    void WarnHotkeys(List<string> failed)
    {
        var text = new List<string>();
        if (failed.Count > 0)
            text.Add(L.F("Следующие сочетания клавиш используются другой программой: {0}.", "The following hotkeys are in use by another app: {0}.", string.Join(", ", failed)));
        if (notAllowed.Count > 0)
            text.Add(L.F("Следующие сочетания клавиш недопустимы и отключены: {0}.", "The following hotkeys are not allowed and have been turned off: {0}.", string.Join(", ", notAllowed)));
        if (text.Count > 0)
            tray.ShowBalloonTip(4000, "KeyFish", string.Join(" ", text) + " " + L.T("Их можно изменить в настройках.", "They can be changed in Settings."), ToolTipIcon.Warning);
    }

    void OnHotkey(int id)
    {
        if (busy) return;
        busy = true;
        hotkeyWindow = Native.GetForegroundWindow();
        textChanged = false;
        try
        {
            // The clipboard is converted in place: nothing is copied from or typed into the window, so it works anywhere
            if (id == HkClipboard) { ConvertClipboard(); return; }
            if (id == HkPause) { TogglePause(true); return; }
            if (W.HasMoreRights(hotkeyWindow)) { NoRightsHere(hotkeyWindow); return; }
            if (id == HkPastePlain) { PastePlain(); return; }
            if (id == HkLayout && watcher.Undo()) return;
            if (!watcher.HotkeyAllowed(hotkeyWindow)) return;   // an excluded app or a password field
            if (!Native.WaitModifiersReleased()) return;      // keys still held: Ctrl+C would become Ctrl+Shift+C
            if (id == HkCase) ChangeCase();
            else if (id == HkTranslit) { string lang = TranslitLanguage(); TransformSelection(s => Transliterator.Convert(s, lang)); }
            else if (id == HkLine) ConvertLine();
            else if (id == HkPlain) PlainSelection();
            else if (id == HkActions) TextMenu();
            else ConvertSelection();
        }
        catch { }
        finally
        {
            busy = false;
            if (textChanged && playSound) Sounds.Play(Sounds.Hotkey);
        }
    }

    // A hotkey changed the text (pasted it, or converted the clipboard): its sound plays
    static bool textChanged;

    // A hotkey in an app run as administrator: Windows won't let KeyFish copy or type there, so say why nothing happens
    // (once per app while KeyFish runs) instead of leaving the user guessing
    readonly HashSet<string> toldNoRights = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void NoRightsHere(IntPtr hwnd)
    {
        string app = W.ProcessName(hwnd);
        if (!toldNoRights.Add(app)) return;
        tray.ShowBalloonTip(8000, "KeyFish",
            L.F("{0} запущена от имени администратора. Windows не разрешает KeyFish читать и изменять текст в этой программе. Для работы в ней запустите KeyFish от имени администратора.",
                "{0} is running as administrator. Windows does not allow KeyFish to read or change text in it. To work in it, run KeyFish as administrator.",
                app.Length > 0 ? app : L.T("Эта программа", "This app")), ToolTipIcon.Info);
    }

    // The window the hotkey was pressed in. Hotkeys work through Ctrl+C, Ctrl+V and selecting text; if the user switches
    // to another window meanwhile (or the window closes), none of that may land there
    static IntPtr hotkeyWindow;

    static bool SameWindow() { return Native.GetForegroundWindow() == hotkeyWindow; }

    // Paste through the clipboard. The pause before Ctrl+V is required: without it the app
    // sometimes pastes the old clipboard contents (seen with Shift+Pause)
    static bool Paste(string text)
    {
        if (!SameWindow() || !SetClipboardText(text)) return false;
        Pump(50);
        if (!SameWindow()) return false;
        Native.Chord(Native.VK_CONTROL, (byte)'V');
        Pump(150);
        textChanged = true;
        return true;
    }

    void TransformSelection(Func<string, string> transform)
    {
        Clip.Saved saved = SaveClipboard();
        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            string result = transform(text);
            if (result != text) Paste(result);
        }
        RestoreClipboard(saved);
    }

    // The selection pasted back as plain text (PlainText), even when its letters stay the same
    void PlainSelection()
    {
        Clip.Saved saved = SaveClipboard();
        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text)) Paste(PlainText.Clean(text));
        RestoreClipboard(saved);
    }

    void ChangeCase()
    {
        Clip.Saved saved = SaveClipboard();

        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            string result = CaseConverter.Next(text);
            if (Paste(result))
            {
                // Select the pasted text again so the next press continues the cycle
                // One caret step per character: an emoji is two UTF-16 units, a line break two characters
                int caretSteps = new System.Globalization.StringInfo(result).LengthInTextElements - CountOf(result, "\r\n");
                if (caretSteps <= 5000 && SameWindow()) Native.SelectLeft(caretSteps);
                Pump(50);
            }
        }

        RestoreClipboard(saved);
    }

    static int CountOf(string s, string sub)
    {
        int n = 0;
        for (int i = s.IndexOf(sub); i >= 0; i = s.IndexOf(sub, i + sub.Length)) n++;
        return n;
    }

    static string GetSelectionOrLastWord()
    {
        string text = CopySelection();
        if (string.IsNullOrEmpty(text))
        {
            // Nothing is selected, so take the last word before the caret
            if (!SameWindow()) return null;
            Native.Chord(Native.VK_CONTROL, Native.VK_SHIFT, Native.VK_LEFT);
            Pump(30);
            text = CopySelection();
        }
        return text;
    }

    // From Latin, transliteration goes into the language of the user's other alphabet: the window's layout if it isn't
    // Latin, otherwise the first such layout installed (Russian if there is none)
    string TranslitLanguage()
    {
        var current = watcher.Layouts.Get(W.LayoutHkl(hotkeyWindow));
        if (current != null && current.Script != "Latin" && current.Lang != null) return current.Lang;
        var other = watcher.AllLayouts.FirstOrDefault(t => t.Script != null && t.Script != "Latin" && t.Lang != null);
        return other != null ? other.Lang : "ru";
    }

    // The last selection switched with Pause, for going on to the next layout with Pause again
    class LayoutCycle
    {
        public IntPtr Hwnd;
        public LayoutTable Source;
        public string Original, Result;
        public int At;
        public readonly List<IntPtr> Tried = new List<IntPtr>();
    }

    LayoutCycle cycle;

    // A whole phrase typed in the wrong layout: select from the cursor to the start of the line and switch it
    void ConvertLine()
    {
        IntPtr target = Native.GetForegroundWindow();
        Clip.Saved saved = SaveClipboard();
        if (!SameWindow()) { RestoreClipboard(saved); return; }
        Native.Chord(Native.VK_SHIFT, Native.VK_HOME);
        Pump(30);
        string text = CopySelection();
        if (!string.IsNullOrEmpty(text))
        {
            LayoutTable layout;
            string result = watcher.ConvertText(text, target, out layout);
            if (result != null && Paste(result)) W.SwitchLayout(target, layout.Hkl);
        }
        RestoreClipboard(saved);
    }

    // The text the user copied, in the other layout, put back into the clipboard to paste anywhere: for places where
    // the selection hotkey can't copy or type (remote desktops, apps run as administrator, some games and web forms).
    // The text isn't shown in the notification: Windows keeps notifications
    void ConvertClipboard()
    {
        string text = null;
        try { text = Clip.Text(); } catch { }
        if (string.IsNullOrEmpty(text) || text.Length > 100000)
        {
            tray.ShowBalloonTip(3000, "KeyFish", L.T("В буфере обмена нет текста.", "There is no text in the clipboard."), ToolTipIcon.Info);
            return;
        }
        LayoutTable layout;
        string result = watcher.ConvertText(text, hotkeyWindow, out layout);
        if (result == null)
        {
            tray.ShowBalloonTip(3000, "KeyFish", L.T("Не удалось определить целевую раскладку текста.", "Could not determine the target layout for the text."), ToolTipIcon.Info);
            return;
        }
        // Pasteable as usual, but kept out of the clipboard history and cloud sync, like KeyFish's other text
        try { if (!Clip.PutText(result)) return; } catch { return; }
        textChanged = true;
        tray.ShowBalloonTip(3000, "KeyFish", L.F("Текст в буфере обмена преобразован в раскладку {0}.",
            "The text in the clipboard has been converted to the {0} layout.", (layout.Name ?? "").Split('-')[0].ToUpperInvariant()), ToolTipIcon.Info);
    }

    void ConvertSelection()
    {
        IntPtr target = Native.GetForegroundWindow();

        Clip.Saved saved = SaveClipboard();

        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            LayoutTable layout, source;
            string result;
            // Pause again on what Pause just made: the same keys in the next installed layout, round to the original
            var c = cycle;
            if (c != null && c.Hwnd == target && Environment.TickCount - c.At < 15000 && text == c.Result)
            {
                var next = watcher.AllLayouts.FirstOrDefault(t => t.Hkl != c.Source.Hkl && !c.Tried.Contains(t.Hkl));
                layout = next ?? c.Source;
                result = next != null ? KeyboardWatcher.Retype(c.Original, c.Source, next) : c.Original;
                source = c.Source;
                text = c.Original;
            }
            else
            {
                result = watcher.ConvertText(text, target, out layout, out source);
                c = null;
            }
            if (result != null && Paste(result))
            {
                W.SwitchLayout(target, layout.Hkl);
                if (c == null) cycle = new LayoutCycle { Hwnd = target, Source = source, Original = text };
                cycle.Result = result;
                cycle.At = Environment.TickCount;
                cycle.Tried.Add(layout.Hkl);
                // Back to the start: the round is over, and the word was right as typed — nothing to learn from it,
                // and what an earlier step of the round taught is taken back
                if (layout.Hkl == cycle.Source.Hkl) { cycle = null; watcher.Unlearn(text); }
                else watcher.Learn(text, result, layout.Lang);
            }
        }

        RestoreClipboard(saved);
    }

    // Apps where Ctrl+Shift+V already has a meaning of its own: editors and IDEs (Markdown preview, the clipboard
    // history), terminals (their paste), Outlook (Move to folder). The keys go on to them
    static readonly HashSet<string> OwnPastePlain = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "Code - Insiders", "devenv", "sublime_text", "idea64", "pycharm64", "rider64", "webstorm64", "clion64", "goland64",
        "phpstorm64", "datagrip64", "rubymine64", "studio64", "WindowsTerminal", "alacritty", "wezterm-gui", "OUTLOOK", "olk"
    };

    // The clipboard's text pasted without its formatting, in any app: put on the clipboard as text alone for the paste,
    // the user's own clipboard back a second later. Where the keys mean something else already, where KeyFish may not
    // touch the text (a password field, an excluded app), or with no text on the clipboard (a picture, files), the
    // keys go on to the app as if KeyFish weren't there
    void PastePlain()
    {
        bool here = !OwnPastePlain.Contains(W.ProcessName(hotkeyWindow)) && watcher.HotkeyAllowed(hotkeyWindow);
        if (!Native.WaitModifiersReleased()) return;
        string text = null;
        if (here) try { text = Clip.Text(); } catch { }
        if (string.IsNullOrEmpty(text)) { PassOn(HkPastePlain); return; }
        Clip.Saved saved = SaveClipboard();
        Paste(text);
        RestoreClipboard(saved);
    }

    // A hotkey's keys sent on to the window in front: let go of for a moment, so they reach the app, and taken back
    void PassOn(int id)
    {
        Keys k = GetHotkey(id - 1);
        var keys = new List<byte>();
        if ((k & Keys.Control) != 0) keys.Add(Native.VK_CONTROL);
        if ((k & Keys.Shift) != 0) keys.Add(Native.VK_SHIFT);
        if ((k & Keys.Alt) != 0) keys.Add(Native.VK_MENU);
        keys.Add((byte)(k & Keys.KeyCode));
        Native.UnregisterHotKey(hotkeys.Handle, id);
        try
        {
            Native.Chord(keys.ToArray());
            Pump(50);
        }
        finally { Native.RegisterHotKey(hotkeys.Handle, id, Modifiers(k) | Native.MOD_NOREPEAT, (uint)(k & Keys.KeyCode)); }
    }

    // The menu of text actions, next to the caret: for the selection or, with nothing selected, the line before the
    // cursor. The result replaces the text; only what would change it can be picked
    void TextMenu()
    {
        IntPtr target = hotkeyWindow;
        Clip.Saved saved = SaveClipboard();
        bool selectedHere = false;
        string text = CopySelection();
        if (string.IsNullOrEmpty(text) && SameWindow())
        {
            Native.Chord(Native.VK_SHIFT, Native.VK_HOME);
            Pump(30);
            text = CopySelection();
            selectedHere = !string.IsNullOrEmpty(text);
        }
        if (string.IsNullOrEmpty(text) || text.Length > 100000) { RestoreClipboard(saved); return; }

        string result = null;
        Action after = null;
        Point at = LayoutBadge.CaretOrMouse();
        Func<string, Tuple<string, Action>> switchLayout = t =>
        {
            LayoutTable layout;
            string r = watcher.ConvertText(t, target, out layout);
            return r == null ? null : Tuple.Create(r, (Action)(() => W.SwitchLayout(target, layout.Hkl)));
        };
        // The menu takes the keyboard only while KeyFish is the active app: an invisible window of its own is
        // brought forward first, and the window the text is in gets the focus back afterwards
        using (var host = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                                     Location = new Point(-32000, -32000), Size = new Size(1, 1) })
        using (var menu = TextMenuFor(text, switchLayout, TranslitLanguage(), (r, a) => { result = r; after = a; }))
        {
            host.Show();
            SetForegroundWindow(host.Handle);
            menu.Show(at);
            foreach (ToolStripItem item in menu.Items)
                if (item is ToolStripMenuItem && item.Enabled) { item.Select(); break; }
            while (menu.Visible) Pump(15);
            host.Hide();
        }
        SetForegroundWindow(target);
        for (int i = 0; i < 40 && !SameWindow(); i++) Pump(25);
        if (result != null) { if (Paste(result) && after != null) after(); }
        else if (selectedHere && SameWindow()) Unselect(target);   // closed: the line KeyFish selected is let go
        RestoreClipboard(saved);
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    // The selection KeyFish made let go, the cursor back at its end, where it was. Browsers and editors move it there
    // with the Right key; a Windows text box would move it from the selection's start, so it is told directly
    static void Unselect(IntPtr window)
    {
        uint pid;
        var info = new W.GUITHREADINFO();
        info.cbSize = Marshal.SizeOf(info);
        if (W.GetGUIThreadInfo(W.GetWindowThreadProcessId(window, out pid), ref info) && info.hwndFocus != IntPtr.Zero &&
            W.ClassName(info.hwndFocus).IndexOf("edit", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            IntPtr sel;
            // EM_GETSEL: the two ends in the low and the high word, the right one either way round as text boxes
            // report it (0xFFFF past that: then the Right key)
            if (SendMessageTimeout(info.hwndFocus, 0x00B0, IntPtr.Zero, IntPtr.Zero, 0x2, 200, out sel) != IntPtr.Zero)
            {
                uint both = (uint)sel.ToInt64();
                int end = (int)Math.Max(both & 0xFFFF, both >> 16);
                if (end < 0xFFFF)
                {
                    IntPtr ignored;
                    SendMessageTimeout(info.hwndFocus, 0x00B1, new IntPtr(end), new IntPtr(end), 0x2, 200, out ignored);   // EM_SETSEL
                    return;
                }
            }
        }
        Native.Chord(Native.VK_RIGHT);
    }

    // switchLayout: the text in the other layout and what to do after pasting it (null: no other layout fits)
    public static ContextMenuStrip TextMenuFor(string text, Func<string, Tuple<string, Action>> switchLayout, string translit,
                                               Action<string, Action> choose)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        ThemedMenuRenderer.Apply(menu);
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        double? value = Calc.Evaluate(text);
        if (value.HasValue)
        {
            string number = Calc.Format(value.Value, text);
            MenuAction(menu.Items, L.F("Вычислить: {0}", "Calculate: {0}", number), text, number, null, Keys.None, choose);
            MenuAction(menu.Items, L.F("Дописать результат: = {0}", "Append the result: = {0}", number), text,
                       text.TrimEnd().TrimEnd('=').TrimEnd() + " = " + number, null, Keys.None, choose);
            menu.Items.Add(new ToolStripSeparator());
        }

        MenuAction(menu.Items, L.T("ВСЕ ПРОПИСНЫЕ", "UPPERCASE"), text, text.ToUpper(culture), null, Keys.None, choose);
        MenuAction(menu.Items, L.T("все строчные", "lowercase"), text, text.ToLower(culture), null, Keys.None, choose);
        MenuAction(menu.Items, L.T("Как в предложении", "Sentence case"), text, TextActions.Sentence(text), null, Keys.None, choose);
        MenuAction(menu.Items, L.T("Каждое Слово С Прописной", "Capitalize Each Word"), text, CaseConverter.ToTitle(text), null, Keys.None, choose);
        menu.Items.Add(new ToolStripSeparator());

        var switched = switchLayout(text);
        MenuAction(menu.Items, L.T("Сменить раскладку", "Switch the layout"), text, switched == null ? null : switched.Item1,
                   switched == null ? null : switched.Item2, GetHotkey(HkLayout - 1), choose);
        MenuAction(menu.Items, L.T("Транслитерация", "Transliterate"), text, Transliterator.Convert(text, translit), null, GetHotkey(HkTranslit - 1), choose);
        // Pasted back as plain text, formatting goes even when the letters stay the same
        MenuAction(menu.Items, L.T("Удалить форматирование", "Clear formatting"), null, PlainText.Clean(text), null, GetHotkey(HkPlain - 1), choose);
        menu.Items.Add(new ToolStripSeparator());

        MenuAction(menu.Items, L.T("Удалить лишние пробелы и пустые строки", "Remove extra spaces and empty lines"), text, TextActions.Spaces(text), null, Keys.None, choose);
        if (text.TrimEnd('\r', '\n').Contains("\n"))
        {
            MenuAction(menu.Items, L.T("Сортировать строки", "Sort lines"), text, TextActions.SortLines(text), null, Keys.None, choose);
            MenuAction(menu.Items, L.T("Удалить повторяющиеся строки", "Remove duplicate lines"), text, TextActions.UniqueLines(text), null, Keys.None, choose);
        }
        else if (text.Length <= 200)
        {
            // Each style shown as the name it makes
            var code = new ToolStripMenuItem(L.T("Имя для кода", "Name for code"));
            ThemedMenuRenderer.Apply(code.DropDown);
            ((ToolStripDropDownMenu)code.DropDown).ShowImageMargin = false;
            foreach (TextActions.Code style in Enum.GetValues(typeof(TextActions.Code)))
            {
                string name = TextActions.CodeName(text, style);
                MenuAction(code.DropDownItems, name.Length > 40 ? name.Substring(0, 40) + "…" : name, text, name, null, Keys.None, choose);
            }
            code.Enabled = code.DropDownItems.Cast<ToolStripItem>().Any(i => i.Enabled);
            menu.Items.Add(code);
        }
        return menu;
    }

    // An item of the text actions menu, with the hotkey that does the same if there is one. Off when the result is the
    // text as it is (unless "same" is null: the action does something even then)
    static void MenuAction(ToolStripItemCollection items, string title, string same, string result, Action after, Keys hotkey,
                           Action<string, Action> choose)
    {
        var item = new ToolStripMenuItem(title.Replace("&", "&&"))
        {
            Enabled = result != null && result != same,
            ShortcutKeyDisplayString = hotkey != Keys.None ? SettingsForm.Format(hotkey) : null
        };
        item.Click += (s, e) => choose(result, after);
        items.Add(item);
    }

    // Wait while still processing messages, so the clipboard and the target window keep working meanwhile
    static void Pump(int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    static string CopySelection()
    {
        if (!SameWindow()) return null;
        uint before = Native.GetClipboardSequenceNumber();
        Native.Chord(Native.VK_CONTROL, (byte)'C');
        for (int i = 0; i < 40; i++)
        {
            Pump(15);
            if (Native.GetClipboardSequenceNumber() != before)
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    Pump(20);
                    try { return Clip.HasText() && !IsLineCopy() ? Clip.Text() : null; }
                    catch { }
                }
                return null;
            }
        }
        return null;
    }

    // With nothing selected, Ctrl+C in VS Code and Visual Studio copies the whole line. That isn't a selection:
    // converting and pasting it would add a converted copy of the line, so the last word is taken instead
    static bool IsLineCopy()
    {
        var formats = Clip.Named(f => f == "VisualStudioEditorOperationsLineCutCopyClipboardTag" || f == "MSDEVLineSelect" ||
                                      f.IndexOf("vscode-editor-data", StringComparison.OrdinalIgnoreCase) >= 0 || f == "Chromium Web Custom MIME Data Format");
        foreach (var kv in formats)
        {
            if (kv.Key == "VisualStudioEditorOperationsLineCutCopyClipboardTag" || kv.Key == "MSDEVLineSelect") return true;
            if (kv.Value == null) continue;
            string s = Encoding.UTF8.GetString(kv.Value) + Encoding.Unicode.GetString(kv.Value);
            if (s.Replace(" ", "").IndexOf("\"isFromEmptySelection\":true", StringComparison.Ordinal) >= 0) return true;
        }
        return false;
    }

    // KeyFish's text, kept out of the clipboard history and cloud sync (see Clip.Put)
    static bool SetClipboardText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { if (Clip.PutText(text)) return true; }
            catch { }
            Pump(30);
        }
        return false;
    }

    static Clip.Saved SaveClipboard()
    {
        FlushRestore();   // the previous hotkey's restore first, or its text would be saved as the user's
        try { return Clip.Save(64L * 1024 * 1024); }
        catch { return null; }
    }

    // The user's clipboard is given back a second later, not right after Ctrl+V: a slow app (a remote desktop, a busy
    // Electron window) may read the clipboard after that and would paste the old contents instead of the fix.
    // If anything else is copied in that second, the user's copy wins and nothing is put back
    static Clip.Saved restoreData;

    static uint restoreSeq;

    static System.Windows.Forms.Timer restoreTimer;

    static void RestoreClipboard(Clip.Saved saved)
    {
        FlushRestore();
        restoreData = saved;
        restoreSeq = Native.GetClipboardSequenceNumber();
        if (restoreTimer == null)
        {
            restoreTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            restoreTimer.Tick += (s, e) => FlushRestore();
        }
        restoreTimer.Stop();
        restoreTimer.Start();
    }

    // Puts the user's clipboard back now, if it is still waiting to be (before the next hotkey and at exit)
    static void FlushRestore()
    {
        if (restoreTimer != null) restoreTimer.Stop();
        var saved = restoreData;
        if (saved == null && restoreSeq == 0) return;
        restoreData = null;
        uint seq = restoreSeq;
        restoreSeq = 0;
        if (Native.GetClipboardSequenceNumber() == seq) PutBack(saved);
    }

    static void PutBack(Clip.Saved saved)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (saved == null || saved.Formats.Count == 0) { Clip.Clear(); return; }
                if (Clip.Put(saved)) return;   // already in the history from the user's own copy
            }
            catch { }
            Pump(30);
        }
    }
}

// The clipboard through Windows' own clipboard functions, not OLE: what another app put there is read as bytes and
// never turned into a .NET object. A broken COM object an app had put on the clipboard took KeyFish down with .NET's
// own error (0xc0000005 in clr.dll, GetCCWFromIUnknown), which nothing in KeyFish could catch
static class Clip
{
    const uint CF_TEXT = 1, CF_DIB = 8, CF_OEMTEXT = 7, CF_UNICODETEXT = 13, CF_HDROP = 15, CF_LOCALE = 16, CF_DIBV5 = 17;

    // What is kept over a hotkey: what people copy and paste between apps, all of it plain memory. An app's own
    // formats (a spreadsheet's Biff12, an embedded object) can be made on demand and run to hundreds of megabytes
    static readonly uint[] KeptStandard = { CF_UNICODETEXT, CF_TEXT, CF_OEMTEXT, CF_LOCALE, CF_DIB, CF_DIBV5, CF_HDROP };
    static readonly HashSet<string> KeptNamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Rich Text Format", "HTML Format", "Csv", "FileName", "FileNameW", "Shell IDList Array", "Preferred DropEffect", "PNG"
    };

    // A copy of the clipboard: the formats kept, as bytes, in the order the app offered them
    public sealed class Saved
    {
        public readonly List<KeyValuePair<uint, byte[]>> Formats = new List<KeyValuePair<uint, byte[]>>();
    }

    // The window that owns what KeyFish puts on the clipboard: a message-only one, made once on the window thread
    static NativeWindow owner;
    static IntPtr Owner
    {
        get
        {
            if (owner == null) { owner = new NativeWindow(); owner.CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); }   // HWND_MESSAGE
            return owner.Handle;
        }
    }

    // Another app may hold the clipboard open for a moment
    static bool Open()
    {
        for (int i = 0; i < 20; i++)
        {
            if (OpenClipboard(Owner)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    public static bool HasText() { return IsClipboardFormatAvailable(CF_UNICODETEXT); }

    // The text on the clipboard, or null
    public static string Text()
    {
        if (!HasText() || !Open()) return null;
        try
        {
            byte[] b = Read(GetClipboardData(CF_UNICODETEXT));
            if (b == null) return null;
            string s = Encoding.Unicode.GetString(b);
            int end = s.IndexOf('\0');
            return end >= 0 ? s.Substring(0, end) : s;
        }
        finally { CloseClipboard(); }
    }

    // The names of the formats on the clipboard and the bytes of the ones asked for (by name)
    public static Dictionary<string, byte[]> Named(Func<string, bool> wanted)
    {
        var found = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Open()) return found;
        try
        {
            for (uint f = EnumClipboardFormats(0); f != 0; f = EnumClipboardFormats(f))
            {
                string name = Name(f);
                if (name != null && wanted(name)) found[name] = HasMemory(f) ? Read(GetClipboardData(f)) : null;
            }
        }
        finally { CloseClipboard(); }
        return found;
    }

    public static Saved Save(long limit)
    {
        var saved = new Saved();
        if (!Open()) return null;
        try
        {
            long kept = 0;
            for (uint f = EnumClipboardFormats(0); f != 0; f = EnumClipboardFormats(f))
            {
                if (Array.IndexOf(KeptStandard, f) < 0 && !(f >= 0xC000 && KeptNamed.Contains(Name(f) ?? ""))) continue;
                IntPtr h = GetClipboardData(f);
                if (h == IntPtr.Zero) continue;
                long size = (long)GlobalSize(h);
                if (size <= 0 || kept + size > limit) continue;
                byte[] b = Read(h);
                if (b == null) continue;
                kept += b.Length;
                saved.Formats.Add(new KeyValuePair<uint, byte[]>(f, b));
            }
        }
        finally { CloseClipboard(); }
        return saved;
    }

    // Puts the formats on the clipboard in place of what is there, kept out of the clipboard history (Win+V) and cloud
    // sync, the way password managers do: KeyFish's text is there only for a moment, and the user's own copy is
    // already in the history
    public static bool Put(Saved data)
    {
        if (!Open()) return false;
        try
        {
            if (!EmptyClipboard()) return false;
            foreach (var kv in data.Formats) Set(kv.Key, kv.Value);
            byte[] no = new byte[4];
            Set(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), no);
            Set(RegisterClipboardFormat("CanIncludeInClipboardHistory"), no);
            Set(RegisterClipboardFormat("CanUploadToCloudClipboard"), no);
            return true;
        }
        finally { CloseClipboard(); }
    }

    public static bool PutText(string text)
    {
        var data = new Saved();
        data.Formats.Add(new KeyValuePair<uint, byte[]>(CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0")));
        return Put(data);
    }

    public static void Clear()
    {
        if (!Open()) return;
        try { EmptyClipboard(); }
        finally { CloseClipboard(); }
    }

    // Standard formats by number; the registered ones (0xC000 and up) by name
    static string Name(uint f)
    {
        if (f < 0xC000) return null;
        var sb = new StringBuilder(256);
        return GetClipboardFormatName(f, sb, sb.Capacity) > 0 ? sb.ToString() : null;
    }

    // Formats whose data is a GDI object or a metafile, not memory: never read as bytes
    static bool HasMemory(uint f)
    {
        return !(f == 2 || f == 3 || f == 9 || f == 14 || (f >= 0x80 && f <= 0x8E) || (f >= 0x200 && f <= 0x3FF));
    }

    static byte[] Read(IntPtr h)
    {
        if (h == IntPtr.Zero) return null;
        long size = (long)GlobalSize(h);
        if (size <= 0 || size > int.MaxValue) return null;
        IntPtr p = GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try
        {
            var b = new byte[size];
            Marshal.Copy(p, b, 0, b.Length);
            return b;
        }
        finally { GlobalUnlock(h); }
    }

    static void Set(uint format, byte[] b)
    {
        IntPtr h = GlobalAlloc(0x0002, (UIntPtr)Math.Max(1, b.Length));   // GMEM_MOVEABLE
        if (h == IntPtr.Zero) return;
        IntPtr p = GlobalLock(h);
        if (p == IntPtr.Zero) { GlobalFree(h); return; }
        Marshal.Copy(b, 0, p, b.Length);
        GlobalUnlock(h);
        if (SetClipboardData(format, h) == IntPtr.Zero) GlobalFree(h);   // taken by Windows only if set
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClipboardFormatName(uint format, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);
}
