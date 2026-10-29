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
                L.T("Раскладка выделенного текста; сразу после автоисправления — отмена (как и Ctrl+Z)",
                    "Switch layout of selected text; right after an auto-fix — undo it (as does Ctrl+Z)"),
                L.T("Раскладка выделенного текста — запасное сочетание", "Switch layout of selected text — alternative keys"),
                L.T("Регистр выделенного текста (aa → Aa → AA)", "Change case of selected text (aa → Aa → AA)"),
                L.T("Транслитерация (привет ⇄ privet)", "Transliterate (привет ⇄ privet)"),
                L.T("Раскладка всей строки до курсора", "Switch layout of the whole line before the cursor"),
                L.T("Раскладка текста в буфере обмена (скопируйте, нажмите, вставьте)", "Switch layout of the text in the clipboard (copy, press, paste)"),
                L.T("Пауза исправлений на час / снять паузу", "Pause corrections for an hour / resume")
            };
        }
    }

    public static readonly Keys[] HotkeyDefaults =
    {
        Keys.Pause, Keys.Control | Keys.Shift | Keys.Space, Keys.Shift | Keys.F3, Keys.Shift | Keys.Pause, Keys.Alt | Keys.Pause,
        Keys.None,   // the clipboard: off until the user picks keys for it
        Keys.None    // pause: the same
    };

    const int HkLayout = 1, HkLayoutAlt = 2, HkCase = 3, HkTranslit = 4, HkLine = 5, HkClipboard = 6, HkPause = 7;

    static Keys GetHotkey(int i) { return (Keys)GetSetting("Hotkey" + (i + 1), (int)HotkeyDefaults[i]); }

    // Registers all hotkeys again; returns the ones taken by other apps
    List<string> RegisterHotkeys()
    {
        hotkeys.UnregisterAll();
        var failed = new List<string>();
        for (int i = 0; i < HotkeyDefaults.Length; i++)
        {
            Keys k = GetHotkey(i);
            if (k == Keys.None || !HotkeyBox.Allowed(k)) continue;   // Enter or a letter stored by an older version or by hand
            uint mods = ((k & Keys.Control) != 0 ? Native.MOD_CONTROL : 0) | ((k & Keys.Shift) != 0 ? Native.MOD_SHIFT : 0) |
                        ((k & Keys.Alt) != 0 ? Native.MOD_ALT : 0);
            if (!hotkeys.Register(i + 1, mods, k & Keys.KeyCode)) failed.Add(SettingsForm.Format(k));
        }
        return failed;
    }

    void WarnHotkeys(List<string> failed)
    {
        if (failed.Count > 0)
            tray.ShowBalloonTip(4000, "KeyFish", L.F("Эти сочетания заняты другой программой: {0}. Их можно поменять в настройках.",
                "These hotkeys are taken by another app: {0}. You can change them in Settings.", string.Join(", ", failed)), ToolTipIcon.Warning);
    }

    void OnHotkey(int id)
    {
        if (busy) return;
        busy = true;
        hotkeyWindow = Native.GetForegroundWindow();
        try
        {
            // The clipboard is converted in place: nothing is copied from or typed into the window, so it works anywhere
            if (id == HkClipboard) { ConvertClipboard(); return; }
            if (id == HkPause) { TogglePause(true); return; }
            if (W.HasMoreRights(hotkeyWindow)) { NoRightsHere(hotkeyWindow); return; }
            if (id == HkLayout && watcher.Undo()) return;
            if (!watcher.HotkeyAllowed(hotkeyWindow)) return;   // an excluded app or a password field
            if (!Native.WaitModifiersReleased()) return;      // keys still held: Ctrl+C would become Ctrl+Shift+C
            if (id == HkCase) ChangeCase();
            else if (id == HkTranslit) { string lang = TranslitLanguage(); TransformSelection(s => Transliterator.Convert(s, lang)); }
            else if (id == HkLine) ConvertLine();
            else ConvertSelection();
        }
        catch { }
        finally { busy = false; }
    }

    // A hotkey in an app run as administrator: Windows won't let KeyFish copy or type there, so say why nothing happens
    // (once per app while KeyFish runs) instead of leaving the user guessing
    readonly HashSet<string> toldNoRights = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void NoRightsHere(IntPtr hwnd)
    {
        string app = W.ProcessName(hwnd);
        if (!toldNoRights.Add(app)) return;
        tray.ShowBalloonTip(8000, "KeyFish",
            L.F("{0} запущена от имени администратора, и Windows не даёт KeyFish видеть и менять в ней текст. Чтобы KeyFish работал и там, запустите его тоже от имени администратора.",
                "{0} runs as administrator, and Windows doesn't let KeyFish see or change text in it. To make KeyFish work there too, run KeyFish as administrator as well.",
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
        return true;
    }

    void TransformSelection(Func<string, string> transform)
    {
        IDataObject saved = SaveClipboard();
        string text = GetSelectionOrLastWord();
        if (!string.IsNullOrEmpty(text))
        {
            string result = transform(text);
            if (result != text) Paste(result);
        }
        RestoreClipboard(saved);
    }

    void ChangeCase()
    {
        IDataObject saved = SaveClipboard();

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
        IDataObject saved = SaveClipboard();
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
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
        if (string.IsNullOrEmpty(text) || text.Length > 100000)
        {
            tray.ShowBalloonTip(3000, "KeyFish", L.T("В буфере обмена нет текста.", "There is no text in the clipboard."), ToolTipIcon.Info);
            return;
        }
        LayoutTable layout;
        string result = watcher.ConvertText(text, hotkeyWindow, out layout);
        if (result == null)
        {
            tray.ShowBalloonTip(3000, "KeyFish", L.T("Не удалось понять, в какую раскладку перевести текст.", "Couldn't tell which layout to switch the text to."), ToolTipIcon.Info);
            return;
        }
        // Pasteable as usual, but kept out of the clipboard history and cloud sync, like KeyFish's other text
        try { Clipboard.SetDataObject(Private(new DataObject(DataFormats.UnicodeText, result)), true); } catch { return; }
        tray.ShowBalloonTip(3000, "KeyFish", L.F("Текст в буфере обмена переведён в раскладку {0} — его можно вставить.",
            "The text in the clipboard is now in the {0} layout, ready to paste.", (layout.Name ?? "").Split('-')[0].ToUpperInvariant()), ToolTipIcon.Info);
    }

    void ConvertSelection()
    {
        IntPtr target = Native.GetForegroundWindow();

        IDataObject saved = SaveClipboard();

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
                watcher.Learn(text, result, layout.Lang);
                if (c == null) cycle = new LayoutCycle { Hwnd = target, Source = source, Original = text };
                cycle.Result = result;
                cycle.At = Environment.TickCount;
                cycle.Tried.Add(layout.Hkl);
                if (layout.Hkl == cycle.Source.Hkl) cycle = null;   // back to the start: the round is over
            }
        }

        RestoreClipboard(saved);
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
                    try { return Clipboard.ContainsText() && !IsLineCopy() ? Clipboard.GetText() : null; }
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
        var data = Clipboard.GetDataObject();
        if (data == null) return false;
        foreach (string f in data.GetFormats(false))
        {
            if (f == "VisualStudioEditorOperationsLineCutCopyClipboardTag" || f == "MSDEVLineSelect") return true;
            if (f.IndexOf("vscode-editor-data", StringComparison.OrdinalIgnoreCase) < 0 && f != "Chromium Web Custom MIME Data Format") continue;
            try
            {
                object raw = data.GetData(f, false);
                var stream = raw as MemoryStream;
                string s = raw as string ?? (stream == null ? "" : Encoding.UTF8.GetString(stream.ToArray()) + Encoding.Unicode.GetString(stream.ToArray()));
                if (s.Replace(" ", "").IndexOf("\"isFromEmptySelection\":true", StringComparison.Ordinal) >= 0) return true;
            }
            catch { }
        }
        return false;
    }

    static bool SetClipboardText(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetDataObject(Private(data), true); return true; }
            catch { Pump(30); }
        }
        return false;
    }

    // KeyFish puts text on the clipboard only for a moment: keep it out of clipboard history (Win+V)
    // and cloud clipboard sync, the way password managers do
    static IDataObject Private(IDataObject data)
    {
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
        return data;
    }

    static readonly HashSet<string> KeptFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText, DataFormats.Rtf, DataFormats.Html, DataFormats.CommaSeparatedValue,
        DataFormats.FileDrop, "FileName", "FileNameW", "Locale", "Shell IDList Array", "Preferred DropEffect",
        DataFormats.Bitmap, DataFormats.Dib, "Format17", "PNG"
    };

    static IDataObject SaveClipboard()
    {
        FlushRestore();   // the previous hotkey's restore first, or its text would be saved as the user's
        try
        {
            var src = Clipboard.GetDataObject();
            if (src == null) return null;
            var copy = new DataObject();
            long kept = 0;
            foreach (string f in src.GetFormats(false))
            {
                // Only what people copy and paste between apps: an app's own formats (a spreadsheet's Biff12, an
                // embedded object) can be made on demand and run to hundreds of megabytes, freezing the hotkey
                if (!KeptFormats.Contains(f)) continue;
                try
                {
                    var d = src.GetData(f, false);
                    if (d == null) continue;
                    var stream = d as Stream;
                    long size = stream != null ? stream.Length : d is string ? ((string)d).Length * 2 : 0;
                    if (kept + size > 64L * 1024 * 1024) continue;
                    kept += size;
                    copy.SetData(f, false, d);
                }
                catch { }
            }
            return copy;
        }
        catch { return null; }
    }

    // The user's clipboard is given back a second later, not right after Ctrl+V: a slow app (a remote desktop, a busy
    // Electron window) may read the clipboard after that and would paste the old contents instead of the fix.
    // If anything else is copied in that second, the user's copy wins and nothing is put back
    static IDataObject restoreData;

    static uint restoreSeq;

    static System.Windows.Forms.Timer restoreTimer;

    static void RestoreClipboard(IDataObject saved)
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

    static void PutBack(IDataObject saved)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (saved == null || saved.GetFormats().Length == 0) Clipboard.Clear();
                else Clipboard.SetDataObject(Private(saved), true);   // already in the history from the user's own copy
                return;
            }
            catch { Pump(30); }
        }
    }
}
