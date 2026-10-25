using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QingShu
{
    internal sealed class Card : Panel
    {
        internal Card()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(246, 247, 251);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = new GraphicsPath())
            {
                const int diameter = 20;
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
                path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
                path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                using (Brush fill = new SolidBrush(Color.White)) e.Graphics.FillPath(fill, path);
                using (Pen border = new Pen(Color.FromArgb(225, 228, 238))) e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        internal const string WindowTitle = "轻输 · 剪贴板逐字输入";
        internal static readonly uint ShowMessage = Native.RegisterWindowMessage("QingShu.ShowWindow.v1");
        private const int HotkeyF8 = 101;
        private const int HotkeyAlternate = 102;
        private const int HotkeyCancel = 103;
        private static readonly Color Accent = Color.FromArgb(79, 70, 229);
        private static readonly Color Ink = Color.FromArgb(31, 36, 54);
        private static readonly Color Muted = Color.FromArgb(109, 115, 135);
        private readonly System.Windows.Forms.Timer clipboardTimer;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem trayEnabled;
        private readonly Icon appIcon;
        private readonly Label hotkeyLabel;
        private readonly Label previewLabel;
        private readonly Label countLabel;
        private readonly TextBox preview;
        private readonly TextBox trial;
        private readonly NumericUpDown interval;
        private readonly CheckBox trim;
        private readonly CheckBox lineMode;
        private readonly CheckBox enabled;
        private readonly CheckBox pin;
        private readonly Label status;
        private readonly Label statusDetail;
        private readonly Label totals;
        private readonly Button startButton;
        private readonly Button stopButton;
        private bool f8Registered;
        private bool alternateRegistered;
        private bool escapeRegistered;
        private bool closing;
        private bool busy;
        private bool clipboardRead;
        private uint clipboardSequence;
        private string clipboardText = "";
        private int nextLine;
        private int successes;
        private int typedCharacters;
        private CancellationTokenSource operation;

        internal bool IsBusy { get { return busy; } }
        internal int Successes { get { return successes; } }
        internal int NextLine { get { return nextLine; } }
        internal string StatusText { get { return status.Text; } }
        internal string StatusDetail { get { return statusDetail.Text; } }
        internal TextBox TrialInput { get { return trial; } }
        internal TextBox PreviewInput { get { return preview; } }
        internal CheckBox LineMode { get { return lineMode; } }
        internal NumericUpDown IntervalControl { get { return interval; } }

        internal MainForm()
        {
            Text = WindowTitle;
            Name = "QingShuMainForm";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            ForeColor = Ink;
            BackColor = Color.FromArgb(246, 247, 251);
            ClientSize = new Size(580, 674);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Icon = appIcon;

            Label logo = AddLabel(this, "A", 24, 22, 48, 45, Accent, 16F, true);
            logo.TextAlign = ContentAlignment.MiddleCenter;
            logo.BackColor = Color.FromArgb(232, 231, 255);
            AddLabel(this, "轻输", 86, 13, 150, 44, Ink, 20F, true);
            AddLabel(this, "复制的内容，按一下就输入。", 88, 56, 330, 24, Muted, 9F, false);
            enabled = new CheckBox { Text = "启用快捷键", Checked = true, AutoSize = true, Location = new Point(446, 36) };
            Controls.Add(enabled);

            Card shortcutCard = AddCard(24, 96, 532, 100);
            AddLabel(shortcutCard, "1  复制单词     →     2  点输入框     →     3  按快捷键", 18, 14, 496, 26, Ink, 10F, true);
            hotkeyLabel = AddLabel(shortcutCard, "F8   /   Ctrl + Alt + V", 18, 51, 242, 30, Accent, 11F, true);
            hotkeyLabel.BackColor = Color.FromArgb(239, 238, 255);
            hotkeyLabel.TextAlign = ContentAlignment.MiddleCenter;
            AddLabel(shortcutCard, "每次读取最新复制的内容", 282, 57, 230, 24, Muted, 9F, false);

            Card previewCard = AddCard(24, 208, 532, 142);
            previewLabel = AddLabel(previewCard, "待输入内容", 18, 14, 386, 24, Ink, 10F, true);
            countLabel = AddLabel(previewCard, "0 字", 425, 15, 86, 24, Muted, 9F, false);
            countLabel.TextAlign = ContentAlignment.TopRight;
            preview = new TextBox
            {
                Name = "clipboardPreview", Location = new Point(20, 48), Size = new Size(492, 75),
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White,
                ForeColor = Ink, ScrollBars = ScrollBars.Vertical, Font = new Font("Microsoft YaHei UI", 11F), TabStop = false
            };
            previewCard.Controls.Add(preview);

            Card optionsCard = AddCard(24, 362, 532, 100);
            AddLabel(optionsCard, "输入间隔", 18, 16, 88, 25, Ink, 9F, true);
            interval = new NumericUpDown
            {
                Name = "characterInterval", Location = new Point(106, 13), Size = new Size(78, 28),
                Minimum = 0, Maximum = 500, Increment = 5, Value = 25, TextAlign = HorizontalAlignment.Center
            };
            optionsCard.Controls.Add(interval);
            AddLabel(optionsCard, "毫秒 / 字", 193, 17, 100, 24, Muted, 9F, false);
            trim = new CheckBox { Name = "trimWhitespace", Text = "去掉首尾空白", Checked = true, AutoSize = true, BackColor = Color.White, Location = new Point(365, 16) };
            lineMode = new CheckBox { Name = "lineMode", Text = "逐行模式：每按一次，输入下一行", AutoSize = true, BackColor = Color.White, Location = new Point(18, 59) };
            pin = new CheckBox { Text = "窗口置顶", AutoSize = true, BackColor = Color.White, Location = new Point(395, 59) };
            optionsCard.Controls.Add(trim);
            optionsCard.Controls.Add(lineMode);
            optionsCard.Controls.Add(pin);

            AddLabel(this, "先试一下：点下方输入框，再按 F8", 26, 477, 500, 23, Muted, 9F, false);
            trial = new TextBox
            {
                Name = "trialInput", Location = new Point(26, 506), Size = new Size(528, 29),
                Font = new Font("Microsoft YaHei UI", 11F), BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(trial);

            status = AddLabel(this, "就绪", 26, 548, 312, 25, Color.FromArgb(25, 128, 91), 10F, true);
            totals = AddLabel(this, "0 次 · 0 字", 350, 550, 204, 24, Muted, 9F, false);
            totals.TextAlign = ContentAlignment.TopRight;
            statusDetail = AddLabel(this, "不会自动回车。输入中按 Esc 可停止。", 26, 578, 528, 23, Muted, 9F, false);
            status.AutoEllipsis = statusDetail.AutoEllipsis = true;

            startButton = new Button
            {
                Text = "3 秒后输入", Location = new Point(24, 612), Size = new Size(410, 40),
                BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
            startButton.FlatAppearance.BorderSize = 0;
            stopButton = new Button
            {
                Text = "停止 · Esc", Location = new Point(446, 612), Size = new Size(110, 40),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, Enabled = false
            };
            stopButton.FlatAppearance.BorderColor = Color.FromArgb(222, 225, 235);
            Controls.Add(startButton);
            Controls.Add(stopButton);

            LoadSettings();
            TopMost = pin.Checked;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开轻输", null, delegate { RestoreWindow(); });
            trayEnabled = new ToolStripMenuItem("启用快捷键") { Checked = true, CheckOnClick = true };
            trayEnabled.CheckedChanged += delegate { enabled.Checked = trayEnabled.Checked; };
            menu.Items.Add(trayEnabled);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            tray = new NotifyIcon { Icon = appIcon, Text = "轻输：F8 输入剪贴板，Esc 停止", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { RestoreWindow(); };

            enabled.CheckedChanged += delegate
            {
                if (operation != null) operation.Cancel();
                trayEnabled.Checked = enabled.Checked;
                RegisterKeys();
                startButton.Enabled = enabled.Checked && !busy;
                SetStatus(enabled.Checked ? "就绪" : "已暂停", enabled.Checked ? "点目标输入框后，按 F8 输入。" : "勾选“启用快捷键”即可恢复。", false);
            };
            pin.CheckedChanged += delegate { TopMost = pin.Checked; SaveSettings(); };
            interval.ValueChanged += delegate { SaveSettings(); };
            trim.CheckedChanged += delegate { nextLine = 0; UpdatePreview(); SaveSettings(); };
            lineMode.CheckedChanged += delegate { nextLine = 0; UpdatePreview(); SaveSettings(); };
            startButton.Click += async delegate { await BeginTypingAsync(true); };
            stopButton.Click += delegate { if (operation != null) operation.Cancel(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };

            clipboardTimer = new System.Windows.Forms.Timer { Interval = 400 };
            clipboardTimer.Tick += delegate { if (!busy) RefreshClipboard(false); };
            clipboardTimer.Start();
            Shown += delegate { RefreshClipboard(true); };
        }

        private Card AddCard(int x, int y, int width, int height)
        {
            Card card = new Card { Location = new Point(x, y), Size = new Size(width, height) };
            Controls.Add(card);
            return card;
        }

        private static Label AddLabel(Control parent, string text, int x, int y, int width, int height, Color color, float size, bool bold)
        {
            Label label = new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(width, height), ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                BackColor = parent is Card ? Color.White : parent.BackColor
            };
            parent.Controls.Add(label);
            return label;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (enabled != null) RegisterKeys();
        }

        private void RegisterKeys()
        {
            if (!IsHandleCreated) return;
            Native.UnregisterHotKey(Handle, HotkeyF8);
            Native.UnregisterHotKey(Handle, HotkeyAlternate);
            f8Registered = alternateRegistered = false;
            if (enabled.Checked)
            {
                f8Registered = Native.RegisterHotKey(Handle, HotkeyF8, Native.MOD_NOREPEAT, Native.VK_F8);
                alternateRegistered = Native.RegisterHotKey(Handle, HotkeyAlternate,
                    Native.MOD_NOREPEAT | Native.MOD_CONTROL | Native.MOD_ALT, 0x56);
            }
            hotkeyLabel.Text = !enabled.Checked ? "快捷键已暂停" :
                f8Registered && alternateRegistered ? "F8   /   Ctrl + Alt + V" :
                f8Registered ? "F8" : alternateRegistered ? "Ctrl + Alt + V" : "快捷键被占用";
            if (enabled.Checked && (!f8Registered || !alternateRegistered))
                SetStatus("部分快捷键被占用", f8Registered || alternateRegistered ? "使用上方仍可用的快捷键。" : "可使用下方“3 秒后输入”按钮。", true);
        }

        protected override void WndProc(ref Message message)
        {
            if ((uint)message.Msg == ShowMessage)
            {
                RestoreWindow();
                return;
            }
            if (message.Msg == Native.WM_HOTKEY)
            {
                int id = message.WParam.ToInt32();
                if (id == HotkeyCancel && operation != null) operation.Cancel();
                else if (id == HotkeyF8 || id == HotkeyAlternate) BeginFromHotkey();
                return;
            }
            base.WndProc(ref message);
        }

        private async void BeginFromHotkey()
        {
            await BeginTypingAsync(false);
        }

        private bool RefreshClipboard(bool force)
        {
            uint currentSequence = Native.GetClipboardSequenceNumber();
            if (!force && clipboardRead && currentSequence == clipboardSequence) return true;
            try
            {
                string current = Clipboard.GetText(TextDataFormat.UnicodeText);
                // Recheck after reading: never commit a snapshot from a changing clipboard.
                if (currentSequence != Native.GetClipboardSequenceNumber())
                {
                    if (force) SetStatus("剪贴板正在更新", "请松开复制快捷键，再按一次 F8。", true);
                    return false;
                }
                if (!clipboardRead || currentSequence != clipboardSequence)
                {
                    clipboardSequence = currentSequence;
                    clipboardText = current;
                    nextLine = 0;
                    clipboardRead = true;
                }
                UpdatePreview();
                return true;
            }
            catch (ExternalException)
            {
                if (force) SetStatus("剪贴板暂时被占用", "稍等一下，再按 F8。", true);
                return false;
            }
        }

        private string PlannedText(out int lineCount)
        {
            if (!lineMode.Checked)
            {
                lineCount = 0;
                return TextTools.Clean(clipboardText, trim.Checked);
            }
            List<string> lines = TextTools.Lines(clipboardText, trim.Checked);
            lineCount = lines.Count;
            return nextLine < lines.Count ? lines[nextLine] : "";
        }

        private void UpdatePreview()
        {
            int lineCount;
            string planned = PlannedText(out lineCount);
            previewLabel.Text = !lineMode.Checked ? "待输入内容" : lineCount == 0 ? "待输入内容 · 暂无行" :
                nextLine >= lineCount ? "逐行模式 · 已全部输入" : "待输入内容 · 第 " + (nextLine + 1) + " / " + lineCount + " 行";
            preview.ForeColor = planned.Length == 0 ? Muted : Ink;
            preview.Text = planned.Length == 0 ? (nextLine > 0 && lineMode.Checked ? "本轮已完成。重新复制即可开始下一轮。" : "复制一个单词，预览会出现在这里。") :
                planned.Length > 1200 ? planned.Substring(0, 1200) + "…" : planned;
            countLabel.Text = TextTools.Count(planned) + " 字";
        }

        private bool TargetAllowed(InputTarget target)
        {
            if (target.Window == IntPtr.Zero) return false;
            uint processId;
            Native.GetWindowThreadProcessId(target.Window, out processId);
            return processId != (uint)Process.GetCurrentProcess().Id || target.Focus == trial.Handle;
        }

        internal async Task BeginTypingAsync(bool delayed)
        {
            if (busy || !enabled.Checked || closing) return;
            InputTarget target = delayed ? null : new InputTarget(Native.GetForegroundWindow());
            if (!delayed && !TargetAllowed(target))
            {
                SetStatus("先点一下输入框", "点学习通的输入框，或上方测试框，再按快捷键。", true);
                return;
            }
            if (!RefreshClipboard(true)) return;
            int lineCount;
            string text = PlannedText(out lineCount);
            if (string.IsNullOrWhiteSpace(text))
            {
                bool finished = lineMode.Checked && lineCount > 0 && nextLine >= lineCount;
                SetStatus(finished ? "所有行已输入" : "剪贴板没有可输入的文字", finished ? "重新复制即可开始下一轮；不会重复循环。" : "先复制一个单词，再按 F8。", true);
                return;
            }
            int count = TextTools.Count(text);
            if (count > TextTools.MaximumLength)
            {
                SetStatus("这段内容太长", "每次最多输入 2,000 字。可少复制一些，或使用逐行模式。", true);
                return;
            }

            uint snapshotSequence = clipboardSequence;
            int snapshotIndex = nextLine;
            bool snapshotLineMode = lineMode.Checked;
            int characterInterval = (int)interval.Value;
            operation = new CancellationTokenSource();
            CancellationToken token = operation.Token;
            busy = true;
            startButton.Enabled = false;
            stopButton.Enabled = true;
            lineMode.Enabled = trim.Enabled = interval.Enabled = false;
            escapeRegistered = Native.RegisterHotKey(Handle, HotkeyCancel, Native.MOD_NOREPEAT, Native.VK_ESCAPE);
            try
            {
                if (delayed)
                {
                    for (int seconds = 3; seconds > 0; seconds--)
                    {
                        SetStatus(seconds + " 秒后开始", "现在点击要输入的位置。Esc 可取消。", false);
                        await Typer.DelayChecked(1000, token);
                    }
                    target = new InputTarget(Native.GetForegroundWindow());
                    if (!TargetAllowed(target)) throw new InvalidOperationException("请在倒计时结束前点击目标输入框。");
                }
                SetStatus("正在输入 · " + count + " 字", "Esc 可停止；请保持输入框焦点。", false);
                await Typer.TypeAsync(text, characterInterval, target, token, delegate(int done, int total)
                {
                    SafeUi(delegate { status.Text = "正在输入 · " + done + " / " + total + " 字"; });
                });
                successes++;
                typedCharacters += count;
                if (snapshotLineMode && snapshotSequence == clipboardSequence && snapshotIndex == nextLine)
                    nextLine++;
                totals.Text = successes + " 次 · " + typedCharacters + " 字";
                SetStatus("已发送 " + count + " 字", snapshotLineMode ?
                    (nextLine >= lineCount ? "本轮已完成。重新复制可开始下一轮。" : "下一次按快捷键，将输入第 " + (nextLine + 1) + " 行。") :
                    "继续复制新单词，再按 F8；也可再次输入同一内容。", false);
            }
            catch (OperationCanceledException)
            {
                if (!closing) SetStatus("已停止", "已输入的部分会保留；逐行模式停留在当前行。", true);
            }
            catch (Exception ex)
            {
                if (!closing) SetStatus("输入已停止", ex.Message, true);
            }
            finally
            {
                if (!closing)
                {
                    if (escapeRegistered) Native.UnregisterHotKey(Handle, HotkeyCancel);
                    escapeRegistered = false;
                    startButton.Enabled = enabled.Checked;
                    stopButton.Enabled = false;
                    lineMode.Enabled = trim.Enabled = interval.Enabled = true;
                    UpdatePreview();
                    RefreshClipboard(false);
                }
                busy = false;
                operation.Dispose();
                operation = null;
            }
        }

        private void SafeUi(Action action)
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (InvalidOperationException) { }
        }

        private void SetStatus(string text, string detail, bool attention)
        {
            status.Text = text;
            status.ForeColor = attention ? Color.FromArgb(169, 99, 21) : Color.FromArgb(25, 128, 91);
            statusDetail.Text = detail;
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private string SettingsPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qingshu.settings.ini"); } }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    string[] pair = line.Split(new[] { '=' }, 2);
                    if (pair.Length != 2) continue;
                    int delay;
                    bool flag;
                    if (pair[0] == "interval" && int.TryParse(pair[1], out delay)) interval.Value = Math.Max(0, Math.Min(500, delay));
                    if (bool.TryParse(pair[1], out flag))
                    {
                        if (pair[0] == "trim") trim.Checked = flag;
                        if (pair[0] == "lines") lineMode.Checked = flag;
                        if (pair[0] == "topmost") pin.Checked = flag;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllLines(SettingsPath, new[] {
                    "interval=" + interval.Value, "trim=" + trim.Checked,
                    "lines=" + lineMode.Checked, "topmost=" + pin.Checked });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            if (operation != null) operation.Cancel();
            clipboardTimer.Stop();
            clipboardTimer.Dispose();
            Native.UnregisterHotKey(Handle, HotkeyF8);
            Native.UnregisterHotKey(Handle, HotkeyAlternate);
            Native.UnregisterHotKey(Handle, HotkeyCancel);
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose();
            tray.Dispose();
            SaveSettings();
            base.OnFormClosing(e);
        }
    }
}
