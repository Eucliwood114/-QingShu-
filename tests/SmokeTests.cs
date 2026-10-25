using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QingShu.Tests
{
    internal static class SmokeProgram
    {
        private static readonly List<string> Results = new List<string>();
        private static readonly string ArtifactDir = AppDomain.CurrentDomain.BaseDirectory;
        private static int exitCode;

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm main = new MainForm())
            {
                main.Shown += async delegate { await RunAsync(main); };
                Application.Run(main);
            }
            return exitCode;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Passed(string name)
        {
            Results.Add("PASS  " + name);
            WriteResults();
        }

        private static void WriteResults()
        {
            File.WriteAllLines(Path.Combine(ArtifactDir, "test-results.txt"), Results.ToArray(), new UTF8Encoding(true));
        }

        private static async Task Until(Func<bool> condition, int timeout, string message)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException(message);
                await Task.Delay(15);
            }
        }

        private static async Task FocusTrial(MainForm main)
        {
            main.Show();
            main.Activate();
            Native.SetForegroundWindow(main.Handle);
            main.TrialInput.Focus();
            await Task.Delay(70);
            Check(Native.GetForegroundWindow() == main.Handle, "Test window is not foreground; no input was sent.");
            Check(Native.FocusWindow(main.Handle) == main.TrialInput.Handle, "Test input is not focused; no input was sent.");
        }

        private static void F8()
        {
            Native.Send(new[] { Native.Key(Native.VK_F8, false), Native.Key(Native.VK_F8, true) });
        }

        private static void Alternate()
        {
            Native.Send(new[] {
                Native.Key(Native.VK_CONTROL, false), Native.Key(Native.VK_MENU, false), Native.Key(0x56, false),
                Native.Key(0x56, true), Native.Key(Native.VK_MENU, true), Native.Key(Native.VK_CONTROL, true) });
        }

        private static async Task Trigger(MainForm main, string expected, bool alternate)
        {
            main.TrialInput.Clear();
            await FocusTrial(main);
            int before = main.Successes;
            if (alternate) Alternate(); else F8();
            await Until(delegate { return main.Successes > before; }, 6500,
                "Input did not complete: " + main.StatusText + "; length=" + main.TrialInput.TextLength);
            await Until(delegate { return !main.IsBusy; }, 2000, "Input remained busy.");
            // SendInput queues events; the receiving control may consume the last WM_CHAR
            // after the completion continuation has already run on this same UI thread.
            await Until(delegate { return main.TrialInput.Text == expected; }, 1000,
                "The receiving control did not consume all queued input.");
            Check(main.TrialInput.Text == expected, "Typed value did not match fixture. Expected length=" + expected.Length + ", actual=" + main.TrialInput.TextLength);
            Check(main.Successes == before + 1, "A single hotkey caused duplicate input.");
        }

        private static async Task Type(MainForm main, string text, string expected, bool alternate)
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
            await Trigger(main, expected, alternate);
        }

        private static DataObject SnapshotClipboard()
        {
            IDataObject original = Clipboard.GetDataObject();
            if (original == null) return null;
            DataObject snapshot = new DataObject();
            foreach (string format in original.GetFormats(false))
            {
                object value = original.GetData(format, false);
                MemoryStream stream = value as MemoryStream;
                Image image = value as Image;
                if (stream != null) value = new MemoryStream(stream.ToArray());
                else if (image != null) value = image.Clone();
                if (value != null) snapshot.SetData(format, false, value);
            }
            return snapshot;
        }

        private static async Task RunAsync(MainForm main)
        {
            DataObject savedClipboard = null;
            bool captured = false;
            Results.Add("QingShu tests (UTC): " + DateTime.UtcNow.ToString("u"));
            try
            {
                savedClipboard = SnapshotClipboard();
                captured = true;
                main.IntervalControl.Value = 15;
                main.LineMode.Checked = false;

                Check(Marshal.SizeOf(typeof(Native.INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "INPUT native layout is wrong.");
                Passed("Win32 INPUT ABI size");
                Check(TextTools.Clean(" \r\napple\tpear\r\n ", true) == "apple pear", "Whitespace normalization failed.");
                Check(TextTools.Clean(" a ", false) == " a ", "Whitespace preservation failed.");
                Check(TextTools.Clean("a\b\0b\uD800c", false) == "abc", "Control or malformed surrogate was retained.");
                Check(TextTools.Lines("\r\n apple\r\n\r\nbanana\u2028orange\n", true).Count == 3, "Line preparation failed.");
                Check(TextTools.Count("a\u0301\U0001F331") == 2, "Unicode text element handling failed.");
                Passed("Whitespace, line endings, control characters, Unicode text elements");

                await Type(main, "apple", "apple", false);
                Passed("Real F8 hotkey and short English input");
                await Type(main, "BANANA 123!? {a}+^%~()[]", "BANANA 123!? {a}+^%~()[]", true);
                Passed("Real Ctrl+Alt+V hotkey, uppercase, punctuation, literal special characters");
                await Type(main, "学习 English café \U0001F331", "学习 English café \U0001F331", false);
                Passed("Unicode input including Chinese, accented letters and surrogate pairs");
                await Type(main, "pear", "pear", false);
                await Type(main, "grape", "grape", false);
                await Trigger(main, "grape", false);
                Check(Clipboard.GetText() == "grape", "Input modified the clipboard.");
                Passed("Repeated copying reads the newest content; unchanged clipboard can be reused");
                await Type(main, " apple\r\nbanana\torange ", "apple banana orange", false);
                Passed("Multiline default sends spaces, without Enter or Tab");

                main.LineMode.Checked = true;
                Clipboard.SetText("\r\n apple\r\n\r\nbanana\norange \n");
                await Trigger(main, "apple", false);
                await Trigger(main, "banana", false);
                await Trigger(main, "orange", false);
                Check(main.NextLine == 3, "Queue index did not advance.");
                main.TrialInput.Clear();
                await FocusTrial(main);
                int before = main.Successes;
                F8();
                await Task.Delay(220);
                Check(!main.IsBusy && main.Successes == before && main.TrialInput.TextLength == 0, "Completed queue repeated unexpectedly.");
                Clipboard.SetText("\r\n apple\r\n\r\nbanana\norange \n");
                await Trigger(main, "apple", false);
                Passed("Per-line queue skips blanks, stops at end, resets after recopying identical text");

                main.LineMode.Checked = false;
                main.IntervalControl.Value = 80;
                Clipboard.SetText(new string('a', 90));
                main.TrialInput.Clear();
                await FocusTrial(main);
                before = main.Successes;
                F8();
                await Until(delegate { return main.IsBusy && main.TrialInput.TextLength >= 3; }, 3000, "Cancellation fixture never started.");
                Native.Send(new[] { Native.Key(Native.VK_ESCAPE, false), Native.Key(Native.VK_ESCAPE, true) });
                await Until(delegate { return !main.IsBusy; }, 2000, "Esc did not cancel.");
                int cancelledLength = main.TrialInput.TextLength;
                await Task.Delay(180);
                Check(cancelledLength >= 3 && cancelledLength < 90 && main.TrialInput.TextLength == cancelledLength && main.Successes == before, "Typing continued after cancellation.");
                Passed("Global Esc cancels real input promptly and does not count a partial operation");

                Clipboard.SetText(new string('b', 90));
                main.TrialInput.Clear();
                await FocusTrial(main);
                F8();
                await Until(delegate { return main.IsBusy && main.TrialInput.TextLength >= 3; }, 3000, "Focus-change fixture never started.");
                main.PreviewInput.Focus();
                await Until(delegate { return !main.IsBusy; }, 2000, "Focus change did not stop input.");
                Check(main.TrialInput.TextLength >= 3 && main.TrialInput.TextLength < 90, "Focus-change input completed instead of stopping.");
                Passed("Changing native input focus stops typing");

                main.LineMode.Checked = true;
                Clipboard.SetText("alpha\nbeta");
                main.TrialInput.Clear();
                await FocusTrial(main);
                F8();
                await Until(delegate { return main.IsBusy && main.TrialInput.TextLength >= 1; }, 3000, "Clipboard-change fixture never started.");
                Clipboard.SetText("new\nnext");
                await Until(delegate { return !main.IsBusy; }, 3000, "Clipboard-change operation did not finish.");
                Check(main.TrialInput.Text == "alpha" && main.NextLine == 0, "Clipboard update corrupted an active operation or queue.");
                await Trigger(main, "new", false);
                Passed("Clipboard changes during typing preserve the current text and reset the next queue");

                main.LineMode.Checked = false;
                main.IntervalControl.Value = 15;
                Clipboard.SetText(new string('x', TextTools.MaximumLength + 1));
                main.TrialInput.Clear();
                await FocusTrial(main);
                before = main.Successes;
                F8();
                await Task.Delay(220);
                Check(!main.IsBusy && main.Successes == before && main.TrialInput.TextLength == 0, "Oversized content was not rejected.");
                Clipboard.Clear();
                F8();
                await Task.Delay(180);
                Check(!main.IsBusy && main.TrialInput.TextLength == 0, "Empty clipboard was typed.");
                Passed("Empty and oversized clipboard contents are rejected without input");

                Clipboard.SetText("held");
                main.TrialInput.Clear();
                await FocusTrial(main);
                before = main.Successes;
                Native.Send(new[] { Native.Key(Native.VK_F8, false) });
                await Task.Delay(200);
                Check(main.TrialInput.TextLength == 0 && main.IsBusy, "Typing started before trigger key was released.");
                Native.Send(new[] { Native.Key(Native.VK_F8, false), Native.Key(Native.VK_F8, true) });
                await Until(delegate { return !main.IsBusy && main.Successes == before + 1; }, 3000, "Held hotkey did not finish exactly once.");
                Check(main.TrialInput.Text == "held", "Held F8 caused duplicate input.");
                Passed("Held/repeated hotkey waits for key release and types exactly once");

                main.IntervalControl.Value = 25;
                Clipboard.SetText("countdown");
                main.TrialInput.Clear();
                Task delayed = main.BeginTypingAsync(true);
                await FocusTrial(main);
                await delayed;
                await Until(delegate { return main.TrialInput.Text == "countdown"; }, 1000,
                    "The countdown receiver did not consume the last queued character.");
                Check(main.TrialInput.Text == "countdown", "Delayed input failed. status=" + main.StatusText + "; detail=" + main.StatusDetail + "; length=" + main.TrialInput.TextLength + "; foregroundIsTest=" + (Native.GetForegroundWindow() == main.Handle) + "; trialHasFocus=" + (Native.FocusWindow(main.Handle) == main.TrialInput.Handle));
                Passed("Three-second countdown types into the selected input");

                await Type(main, "apple", "apple", false);
                main.ActiveControl = null;
                using (Bitmap bitmap = new Bitmap(main.Width, main.Height))
                {
                    main.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(Path.Combine(ArtifactDir, "app-preview.png"), ImageFormat.Png);
                }
                Results.Add("ALL TESTS PASSED");
            }
            catch (Exception ex)
            {
                exitCode = 1;
                Results.Add("FAIL  " + ex.ToString());
                using (Bitmap bitmap = new Bitmap(main.Width, main.Height))
                {
                    main.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(Path.Combine(ArtifactDir, "app-preview.png"), ImageFormat.Png);
                }
            }
            finally
            {
                // Always release keys used by this harness, but only if our own window is foreground.
                if (Native.GetForegroundWindow() == main.Handle)
                    Native.Send(new[] { Native.Key(Native.VK_F8, true), Native.Key(Native.VK_ESCAPE, true),
                        Native.Key(0x56, true), Native.Key(Native.VK_MENU, true), Native.Key(Native.VK_CONTROL, true) });
                if (captured)
                {
                    try
                    {
                        if (savedClipboard == null) Clipboard.Clear();
                        else Clipboard.SetDataObject(savedClipboard, true);
                        Results.Add("Original clipboard restored.");
                    }
                    catch (Exception ex) { exitCode = 1; Results.Add("FAIL  Clipboard restoration: " + ex.Message); }
                }
                WriteResults();
                main.Close();
            }
        }
    }
}
