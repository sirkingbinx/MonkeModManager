using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace MonkeModManager
{
    internal static class Program
    {
        public const string Version = "1.4.4";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            var newVer =
                FormMain.DownloadSite("https://raw.githubusercontent.com/sirkingbinx/MonkeModManager/refs/heads/master/update.txt")
                    .Trim()
                    .Replace("\n", string.Empty);

            if (newVer != Version)
            {
                MessageBox.Show("Your version of the mod installer is outdated! Please download the new one!",
                    "Update available!", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                Process.Start("https://github.com/sirkingbinx/MonkeModManager/releases/latest");
                Process.GetCurrentProcess().Kill();
                Environment.Exit(0);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FormMain());
        }
    }
}
