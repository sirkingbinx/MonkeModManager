using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Windows.Forms;

namespace MonkeModManager
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            var newVer = DownloadSite("https://raw.githubusercontent.com/sirkingbinx/MonkeModManager/refs/heads/master/update.txt");
            newVer = newVer.Substring(0, newVer.Length - 1);
            var vStr = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            vStr = vStr.Substring(0, vStr.Length - 2);
            if (newVer != vStr)
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

        private static string DownloadSite(string URL)
        {
            try
            {
                HttpWebRequest RQuest = (HttpWebRequest)HttpWebRequest.Create(URL);
                RQuest.Method = "GET";
                RQuest.KeepAlive = true;
                RQuest.ContentType = "application/x-www-form-urlencoded";
                RQuest.Referer = "";
                RQuest.UserAgent = "Monke-Mod-Manager";
                RQuest.Proxy = null;

                HttpWebResponse Response = (HttpWebResponse)RQuest.GetResponse();
                StreamReader Sr = new StreamReader(Response.GetResponseStream());
                string Code = Sr.ReadToEnd();
                Sr.Close();
                return Code;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("403"))
                {
                    MessageBox.Show(
                        "Failed to update version info, GitHub has rate limited you, please check back in 15 - 30 minutes",
                        "MonkeModManager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show("Failed to update version info, please check your internet connection", "MonkeModManager",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                Process.GetCurrentProcess().Kill();
                return null;
            }
        }
    }
}
