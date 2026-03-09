using Microsoft.Win32;
using MonkeModManager.Internals;
using MonkeModManager.Internals.SimpleJSON;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Lifetime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace MonkeModManager
{
    public partial class FormMain : Form
    {

        private const string BaseEndpoint = "https://api.github.com/repos/";
        private const Int16 CurrentVersion = 4;
        private List<ReleaseInfo> releases;
        Dictionary<string, int> groups = new Dictionary<string, int>();
        private string InstallDirectory = @"";
        public bool isSteam = true;
        public bool platformDetected = false;

        public FormMain()
        {
            InitializeComponent();
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            LocationHandler();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            releases = new List<ReleaseInfo>();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            labelVersion.Text = "Monke Mod Manager v" + version.Substring(0, version.Length - 2);
            new Thread(() =>
            {
                LoadRequiredPlugins();
            }).Start();
        }

        #region ReleaseHandling

        private void LoadReleases()
        {

            var decoded = JSON.Parse(DownloadSite("https://raw.githubusercontent.com/sirkingbinx/MonkeModManager/refs/heads/master/mods.json"));
            
            var allMods = decoded["mods"].AsArray;
            var allGroups = decoded["groups"].AsArray;

            for (int i = 0; i < allMods.Count; i++)
            {
                JSONNode current = allMods[i];
                ReleaseInfo release = new ReleaseInfo(current["name"], current["author"], current["gitPath"], current["version"], current["group"], current["browser_download_url"], current["melonLoaderType"], current["dependencies"].AsArray);
                releases.Add(release);
            }

            for (int i = 0; i < allGroups.Count; i++)
            {
                JSONNode current = allGroups[i];
                if (releases.Any(x => x.Group == current["name"]))
                {
                    groups.Add(current["name"], groups.Count());
                }
            }

            groups.Add("Uncategorized", groups.Count());

            foreach (ReleaseInfo release in releases)
            {
                foreach (string dep in release.Dependencies)
                {
                    releases.Where(x => x.Name == dep).FirstOrDefault()?.Dependents.Add(release.Name);
                }
            }
            //WriteReleasesToDisk();
        }

        private void LoadRequiredPlugins()
        {
            CheckVersion();
            UpdateStatus("Getting latest version info...");
            LoadReleases();
            this.Invoke((MethodInvoker)(() =>
            {//Invoke so we can call from current thread
             //Update checkbox's text
                Dictionary<string, int> includedGroups = new Dictionary<string, int>();

                for (int i = 0; i < groups.Count(); i++)
                {
                    var key = groups.First(x => x.Value == i).Key;
                    var value = listViewMods.Groups.Add(new ListViewGroup(key, HorizontalAlignment.Left));
                    groups[key] = value;
                }

                foreach (ReleaseInfo release in releases)
                {
                    ListViewItem item = new ListViewItem();
                    item.Text = release.Name;
                    if (!String.IsNullOrEmpty(release.Version)) item.Text = $"{release.Name} - {release.Version}";
                    item.SubItems.Add(release.Author);
                    item.Tag = release;
                    if (release.Install)
                    {
                        listViewMods.Items.Add(item);
                    }
                    CheckDefaultMod(release, item);

                    if (release.Group == null || !groups.ContainsKey(release.Group))
                    {
                        item.Group = listViewMods.Groups[groups["Uncategorized"]];
                    }
                    else if (groups.ContainsKey(release.Group))
                    {
                        int index = groups[release.Group];
                        item.Group = listViewMods.Groups[index];
                    }
                    else
                    {
                        //int index = listViewMods.Groups.Add(new ListViewGroup(release.Group, HorizontalAlignment.Left));
                        //item.Group = listViewMods.Groups[index];
                    }
                }

                tabControlMain.Enabled = true;
                buttonInstall.Enabled = true;

            }));
           
            UpdateStatus("Release info updated!");
        }

#endregion // ReleaseHandling

        #region Installation

        private void Install()
        {
            ChangeInstallButtonState(false);
            UpdateStatus("Starting install sequence...");
            foreach (ReleaseInfo release in releases.Where(r => r.Install))
            {
                UpdateStatus(string.Format("Downloading...{0}", release.Name));
                byte[] file = DownloadFile(release.Link);
                UpdateStatus(string.Format("Installing...{0}", release.Name));
                string fileName = Path.GetFileName(release.Link);
                if (Path.GetExtension(fileName).Equals(".dll"))
                {
                    var pluginsPath = Path.Combine(InstallDirectory, release.MelonLoader ? $"MLLoader\\{release.MLInstallPath}" : @"BepInEx\plugins");
                    if (!Directory.Exists(pluginsPath)) Directory.CreateDirectory(pluginsPath);
                    string dir = Path.Combine(pluginsPath, Regex.Replace(release.Name, @"\s+", string.Empty));
                    
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    
                    File.WriteAllBytes(Path.Combine(dir, fileName), file);

                    var dllFile = Path.Combine(InstallDirectory, @"BepInEx\plugins", fileName);
                    if (File.Exists(dllFile))
                    {
                        File.Delete(dllFile);
                    }
                }
                else
                {
                    UnzipFile(file, InstallDirectory);
                }
                UpdateStatus(string.Format("Installed {0}!", release.Name));
            }
            UpdateStatus("Install complete!");
            ChangeInstallButtonState(true);
        }

        void InstallMod(string modName)
        {
            var release = releases.First(r => r.Name == modName);

            UpdateStatus(string.Format("Downloading...{0}", release.Name));
            byte[] file = DownloadFile(release.Link);
            UpdateStatus(string.Format("Installing...{0}", release.Name));
            string fileName = Path.GetFileName(release.Link);
            if (Path.GetExtension(fileName).Equals(".dll"))
            {
                var pluginsPath = Path.Combine(InstallDirectory, release.MelonLoader ? $"MLLoader\\{release.MLInstallPath}" : @"BepInEx\plugins");
                if (!Directory.Exists(pluginsPath)) Directory.CreateDirectory(pluginsPath);
                string dir = Path.Combine(pluginsPath, Regex.Replace(release.Name, @"\s+", string.Empty));

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllBytes(Path.Combine(dir, fileName), file);

                var dllFile = Path.Combine(InstallDirectory, @"BepInEx\plugins", fileName);
                if (File.Exists(dllFile))
                {
                    File.Delete(dllFile);
                }
            }
            else
            {
                UnzipFile(file, InstallDirectory);
            }
            UpdateStatus(string.Format("Installed {0}!", release.Name));
        }

        #endregion // Installation

        #region UIEvents

        private void buttonInstall_Click(object sender, EventArgs e)
        {
            new Thread(() =>
            {
                Install();
            }).Start();
        }

        private void buttonFolderBrowser_Click(object sender, EventArgs e)
        {
            using (var fileDialog = new OpenFileDialog())
            {
                fileDialog.FileName = "Gorilla Tag.exe";
                fileDialog.Filter = "Exe Files (.exe)|*.exe|All Files (*.*)|*.*";
                fileDialog.FilterIndex = 1;
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    string path = fileDialog.FileName;
                    if (Path.GetFileName(path).Equals("Gorilla Tag.exe"))
                    {
                        InstallDirectory = Path.GetDirectoryName(path);
                        textBoxDirectory.Text = InstallDirectory;
                    }
                    else
                    {
                        MessageBox.Show("That's not Gorilla Tag.exe! please try again!", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                }

            }
        }

        private void listViewMods_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            ReleaseInfo release = (ReleaseInfo)e.Item.Tag;

            if (release.Dependencies.Count > 0)
            {
                foreach (ListViewItem item in listViewMods.Items)
                {
                    var plugin = (ReleaseInfo)item.Tag;

                    if (plugin.Name == release.Name) continue;

                    // if this depends on plugin
                    if (release.Dependencies.Contains(plugin.Name))
                    {
                        if (e.Item.Checked)
                        {
                            item.Checked = true;
                            item.ForeColor = System.Drawing.Color.DimGray;
                        }
                        else
                        {
                            release.Install = false;
                            if (releases.Count(x => plugin.Dependents.Contains(x.Name) && x.Install) <= 1)
                            {
                                item.Checked = false;
                                item.ForeColor = System.Drawing.Color.Black;
                            }
                        }
                    }
                }
            }

            // don't allow user to uncheck if a dependent is checked
            if (release.Dependents.Count > 0)
            {
                if (releases.Count(x => release.Dependents.Contains(x.Name) && x.Install) > 0)
                {
                    e.Item.Checked = true;
                }
            }

            if (release.Name == "BepInEx") { e.Item.Checked = true; }
            release.Install = e.Item.Checked;
        }

         private void listViewMods_DoubleClick(object sender, EventArgs e)
        {
            OpenLinkFromRelease();
        }

        private void buttonModInfo_Click(object sender, EventArgs e)
        {
            OpenLinkFromRelease();
        }

        private void viewInfoToolStripMenuItem_Click(object sender, EventArgs e)
         {
             OpenLinkFromRelease();
         }

        private void listViewMods_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (listViewMods.SelectedItems.Count > 0)
            {
                buttonModInfo.Enabled = true;
            }
            else
            {
                buttonModInfo.Enabled = false;
            }
        }

        private void buttonUninstallAll_Click(object sender, EventArgs e)
        {
            var confirmResult = MessageBox.Show(
                "You are about to delete all your mods (including hats and materials). This cannot be undone!\n\nAre you sure you wish to continue?",
                "Confirm Delete",
                MessageBoxButtons.YesNo);

            if (confirmResult == DialogResult.Yes)
            {
                UpdateStatus("Uninstalling all mods");

                var pluginsPath = Path.Combine(InstallDirectory, @"BepInEx\plugins");

                try
                {
                    foreach (var d in Directory.GetDirectories(pluginsPath))
                    {
                        Directory.Delete(d, true);
                    }

                    foreach (var f in Directory.GetFiles(pluginsPath))
                    {
                        File.Delete(f);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Something went wrong!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateStatus("Failed to uninstall mods.");
                    return;
                }

                UpdateStatus("All mods uninstalled successfully!");
            }
        }

        private void buttonBackupMods_Click(object sender, EventArgs e)
        {
            var pluginsPath = Path.Combine(InstallDirectory, @"BepInEx\plugins");

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                InitialDirectory = InstallDirectory,
                FileName = $"Mod Backup",
                Filter = "ZIP Folder (.zip)|*.zip",
                Title = "Save Mod Backup"
            };

            if (saveFileDialog.ShowDialog() == DialogResult.OK && saveFileDialog.FileName != "")
            {
                UpdateStatus("Backing up mods...");
                try
                {
                    if (File.Exists(saveFileDialog.FileName)) File.Delete(saveFileDialog.FileName);
                    ZipFile.CreateFromDirectory(pluginsPath, saveFileDialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Something went wrong!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateStatus("Failed to back up mods.");
                    return;
                }
                UpdateStatus("Successfully backed up mods!");
            }
        }

        private void buttonBackupCosmetics_Click(object sender, EventArgs e)
        {
            var pluginsPath = Path.Combine(InstallDirectory, @"BepInEx\plugins");

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                InitialDirectory = InstallDirectory,
                FileName = $"Cosmetics Backup",
                Filter = "ZIP Folder (.zip)|*.zip",
                Title = "Save Cosmetics Backup"
            };

            if (saveFileDialog.ShowDialog() == DialogResult.OK && saveFileDialog.FileName != "")
            {
                UpdateStatus("Backing up cosmetics...");
                if (File.Exists(saveFileDialog.FileName)) File.Delete(saveFileDialog.FileName);
                try
                {
                    ZipFile.CreateFromDirectory(Path.Combine(pluginsPath, @"GorillaCosmetics\Hats"), saveFileDialog.FileName, CompressionLevel.Optimal, true);
                    using (ZipArchive archive = ZipFile.Open(saveFileDialog.FileName, ZipArchiveMode.Update))
                    {
                        foreach (var f in Directory.GetFiles(Path.Combine(pluginsPath, @"GorillaCosmetics\Materials")))
                        {
                            archive.CreateEntryFromFile(f, $"{Path.Combine("Materials", Path.GetFileName(f))}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Something went wrong!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateStatus("Failed to restore cosmetics.");
                    return;
                }
                UpdateStatus("Backed up cosmetics!");
            }
        }

        private void buttonRestoreMods_Click(object sender, EventArgs e)
        {
            using (var fileDialog = new OpenFileDialog())
            {
                fileDialog.InitialDirectory = InstallDirectory;
                fileDialog.FileName = "Mod Backup.zip";
                fileDialog.Filter = "ZIP Folder (.zip)|*.zip";
                fileDialog.FilterIndex = 1;
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (!Path.GetExtension(fileDialog.FileName).Equals(".zip", StringComparison.InvariantCultureIgnoreCase))
                    {
                        MessageBox.Show("Invalid file!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateStatus("Failed to restore mods.");
                        return;
                    }
                    var pluginsPath = Path.Combine(InstallDirectory, @"BepInEx\plugins");
                    try
                    {
                        UpdateStatus("Restoring mods...");
                        using (var archive = ZipFile.OpenRead(fileDialog.FileName))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                var directory = Path.Combine(InstallDirectory, @"BepInEx\plugins", Path.GetDirectoryName(entry.FullName));
                                if (!Directory.Exists(directory))
                                {
                                    Directory.CreateDirectory(directory);
                                }

                                entry.ExtractToFile(Path.Combine(pluginsPath, entry.FullName), true);
                            }
                        }
                        UpdateStatus("Successfully restored mods!");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Something went wrong!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateStatus("Failed to restore mods.");
                    }
                }
            }
        }

        private void buttonRestoreCosmetics_Click(object sender, EventArgs e)
        {
            using (var fileDialog = new OpenFileDialog())
            {
                fileDialog.InitialDirectory = InstallDirectory;
                fileDialog.FileName = "Cosmetics Backup.zip";
                fileDialog.Filter = "ZIP Folder (.zip)|*.zip";
                fileDialog.FilterIndex = 1;
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (!Path.GetExtension(fileDialog.FileName).Equals(".zip", StringComparison.InvariantCultureIgnoreCase))
                    {
                        MessageBox.Show("Invalid file!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateStatus("Failed to restore co0smetics.");
                        return;
                    }
                    var cosmeticsPath = Path.Combine(InstallDirectory, @"BepInEx\plugins\GorillaCosmetics");
                    try
                    {
                        UpdateStatus("Restoring cosmetics...");
                        using (var archive = ZipFile.OpenRead(fileDialog.FileName))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                var directory = Path.Combine(InstallDirectory, @"BepInEx\plugins\GorillaCosmetics", Path.GetDirectoryName(entry.FullName));
                                if (!Directory.Exists(directory))
                                {
                                    Directory.CreateDirectory(directory);
                                }

                                entry.ExtractToFile(Path.Combine(cosmeticsPath, entry.FullName), true);
                            }
                        }
                        UpdateStatus("Successfully restored cosmetics!");
                    }
                    catch
                    {
                        MessageBox.Show("Something went wrong!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateStatus("Failed to restore cosmetics.");
                    }
                }
            }
        }

        #region Folders

        private void buttonOpenGameFolder_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(InstallDirectory))
                Process.Start(InstallDirectory);
        }

        private void buttonOpenConfigFolder_Click(object sender, EventArgs e)
        {
            var configDirectory = Path.Combine(InstallDirectory, @"BepInEx\config");
            if (Directory.Exists(configDirectory))
                Process.Start(configDirectory);
        }

        private void buttonOpenBepInExFolder_Click(object sender, EventArgs e)
        {
            var BepInExDirectory = Path.Combine(InstallDirectory, "BepInEx");
            if (Directory.Exists(BepInExDirectory))
                Process.Start(BepInExDirectory);
        }

        #endregion // Folders

        private void buttonOpenWiki_Click(object sender, EventArgs e)
        {
            Process.Start("https://gorillatagmodding.burrito.software/");
        }

        private void buttonDiscordLink_Click(object sender, EventArgs e)
        {
            Process.Start("https://discord.gg/monkemod");
        }

        private void installBepModButton_Click(object sender, EventArgs e)
        {
            installModFromSystem(true);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            installModFromSystem(false);
        }

        private void mlFolder_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(Path.Combine(InstallDirectory, "MLLoader")))
                InstallMod("BepInEx.MelonLoader.Loader");

            Process.Start(Path.Combine(InstallDirectory, "MLLoader"));
        }

        #endregion // UIEvents

        #region Helpers

        private CookieContainer PermCookie;
        private string DownloadSite(string URL)
        {
            try
            {
                if (PermCookie == null) { PermCookie = new CookieContainer(); }
                HttpWebRequest RQuest = (HttpWebRequest)HttpWebRequest.Create(URL);
                RQuest.Method = "GET";
                RQuest.KeepAlive = true;
                RQuest.CookieContainer = PermCookie;
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
                    MessageBox.Show("Failed to update version info, GitHub has rate limited you, please check back in 15 - 30 minutes", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show("Failed to update version info, please check your internet connection", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Process.GetCurrentProcess().Kill();
                return null;
            }
        }

        private void UnzipFile(byte[] data, string directory)
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                using (var unzip = new Unzip(ms))
                {
                    unzip.ExtractToDirectory(directory);
                }
            }
        }

        private byte[] DownloadFile(string url)
        {
            WebClient client = new WebClient();
            client.Proxy = null;
            return client.DownloadData(url);
        }

        private void UpdateStatus(string status)
        {
            string formattedText = string.Format("Status: {0}", status);
            this.Invoke((MethodInvoker)(() =>
            { //Invoke so we can call from any thread
                labelStatus.Text = formattedText;
            }));
        }
  
        private void NotFoundHandler()
        {
            bool found = false;
            while (found == false)
            {
                using (var fileDialog = new OpenFileDialog())
                {
                    fileDialog.FileName = "Gorilla Tag.exe";
                    fileDialog.Filter = "Exe Files (.exe)|*.exe|All Files (*.*)|*.*";
                    fileDialog.FilterIndex = 1;
                    if (fileDialog.ShowDialog() == DialogResult.OK)
                    {
                        string path = fileDialog.FileName;
                        if (Path.GetFileName(path).Equals("Gorilla Tag.exe"))
                        {
                            InstallDirectory = Path.GetDirectoryName(path);
                            textBoxDirectory.Text = InstallDirectory;
                            found = true;
                            SetSavedLocation(InstallDirectory);
                        }
                        else
                        {
                            MessageBox.Show("That's not Gorilla Tag.exe! please try again!", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        Process.GetCurrentProcess().Kill();
                    }
                }
            }
        }

        private void CheckVersion()
        {
            UpdateStatus("Checking for updates...");
            Int16 version = Convert.ToInt16(DownloadSite("https://raw.githubusercontent.com/sirkingbinx/MonkeModManager/master/update.txt"));
            if (version > CurrentVersion)
            {
                this.Invoke((MethodInvoker)(() =>
                {
                    MessageBox.Show("Your version of the mod installer is outdated! Please download the new one!", "Update available!", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    Process.Start("https://github.com/sirkingbinx/MonkeModManager/releases/latest");
                    Process.GetCurrentProcess().Kill();
                    Environment.Exit(0);
                }));
            }
        }

        private void ChangeInstallButtonState(bool enabled)
        {
            this.Invoke((MethodInvoker)(() =>
                {
                    buttonInstall.Enabled = enabled;
                }));
        }

        private void OpenLinkFromRelease()
        {
            if (listViewMods.SelectedItems.Count > 0)
            {
                ReleaseInfo release = (ReleaseInfo)listViewMods.SelectedItems[0].Tag;
                UpdateStatus($"Opening GitHub page for {release.Name}");
                Process.Start(string.Format("https://github.com/{0}", release.GitPath));
            }
            
        }

#endregion // Helpers

        #region Registry

        private void LocationHandler()
        {
            var knownPath = GetSavedLocation();
            if (knownPath == "")
                knownPath = GetSteamLocation();

            if (knownPath != "" && Directory.Exists(knownPath) && File.Exists(knownPath + @"\Gorilla Tag.exe"))
            {
                if (File.Exists(knownPath + @"\Gorilla Tag.exe"))
                {
                    textBoxDirectory.Text = knownPath;
                    InstallDirectory = knownPath;
                    platformDetected = true;
                    return;
                }
            }

            ShowErrorFindingDirectoryMessage();
        }
        private void ShowErrorFindingDirectoryMessage()
        {
            MessageBox.Show("We couldn't seem to find your Gorilla Tag installation, please press \"OK\" and point us to it", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            NotFoundHandler();
            this.TopMost = true;
        }
        private string GetSteamLocation()
        {
            return (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1533390", @"InstallLocation", "");
        }

        private string GetSavedLocation()
        {
            return (string)Registry.GetValue(@"HKEY_CURRENT_USER\Software\SirKingBinx\MonkeModManager", @"InstallPath", "");
        }

        private void SetSavedLocation(string path)
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\SirKingBinx\MonkeModManager", "InstallPath", path);
        }

        private void CheckDefaultMod(ReleaseInfo release, ListViewItem item)
        {
            if (release.Name == "BepInEx")
            {
                item.Checked = true;
                item.ForeColor = System.Drawing.Color.DimGray;
            }
            else
            {
                release.Install = false;
            }
        }
        #endregion // Registry

        #region InstallHelpers

        private void installModFromSystem(bool isBepInEx = true)
        {
            using (var fileDialog = new OpenFileDialog())
            {
                fileDialog.Filter = "Plugins (*.dll)|*.dll|All Files (*.*)|*.*";
                fileDialog.FilterIndex = 1;

                if (fileDialog.ShowDialog() != DialogResult.OK)
                    return;

                if (!isBepInEx)
                    InstallMod("BepInEx.MelonLoader.Loader");

                var path = fileDialog.FileName;
                var friendlyName = fileDialog.SafeFileName ?? path;

                var pluginsPath = Path.Combine(InstallDirectory, isBepInEx ? @"BepInEx\plugins" : @"MLLoader\Mods");
                if (!Directory.Exists(pluginsPath)) Directory.CreateDirectory(pluginsPath);
                string dir = Path.Combine(pluginsPath, isBepInEx ? Regex.Replace(Path.GetFileNameWithoutExtension(friendlyName), @"\s+", string.Empty) : "");

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllBytes(Path.Combine(dir, friendlyName), File.ReadAllBytes(path));

                var dllFile = Path.Combine(InstallDirectory, @"BepInEx\plugins", friendlyName);
                if (File.Exists(dllFile))
                {
                    File.Delete(dllFile);
                }
            }
        }
        #endregion
    }
}
