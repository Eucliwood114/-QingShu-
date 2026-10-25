using System;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("轻输")]
[assembly: System.Reflection.AssemblyDescription("剪贴板逐字输入工具")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace QingShu
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\QingShu.ClipboardTyper.v1", out created))
            {
                if (!created)
                {
                    IntPtr existing = Native.FindWindow(null, MainForm.WindowTitle);
                    if (existing != IntPtr.Zero)
                    {
                        Native.PostMessage(existing, MainForm.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                        Native.SetForegroundWindow(existing);
                    }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show(e.Exception.Message, "轻输", MessageBoxButtons.OK, MessageBoxIcon.Information);
                };
                Application.Run(new MainForm());
                mutex.ReleaseMutex();
            }
        }
    }
}
