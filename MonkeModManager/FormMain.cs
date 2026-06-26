using Microsoft.Win32;
using MonkeModManager.Internals;
using MonkeModManager.Internals.SimpleJSON;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace MonkeModManager
{
    public partial class FormMain : Form
    {
        public List<ReleaseInfo> Releases = [];
        public List<string> Groups = [];
        public string InstallDirectory = "";
        public bool PlatformDetected;

        public FormMain()
        {
            InitializeComponent();
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            LocationHandler();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            labelVersion.Text = "MonkeModManager v" + version.Substring(0, version.Length - 2);

            new Thread(() =>
            {
                LoadRequiredPlugins();
            }).Start();
        }
         
        #region ReleaseHandling

        private void LoadReleases()
        {
            listViewMods.Items.Clear();

            var decoded =
                JSON.Parse(DownloadSite(
                    "https://raw.githubusercontent.com/sirkingbinx/MonkeModManager/refs/heads/master/mods.json"));
            
            var allMods = decoded["mods"].AsArray;
            var allGroups = decoded["groups"].AsArray;

            for (int i = 0; i < allMods.Count; i++)
            {
                JSONNode current = allMods[i];
                ReleaseInfo release = new ReleaseInfo(current["name"], current["author"], current["gitPath"],
                    current["version"], current["group"], current["browser_download_url"],
                    current["dependencies"].AsArray);

                Releases.Add(release);
            }

            for (int i = 0; i < allGroups.Count; i++)
            {
                JSONNode current = allGroups[i];
                if (Releases.Any(x => x.Group == current["name"]) && !Groups.Contains(current["name"]))
                {
                    Groups.Add(current["name"]);
                }
            }

            if (!Groups.Contains("Uncategorized"))
                Groups.Add("Uncategorized");

            foreach (var release in Releases)
                release.Dependencies.ForEach(dep =>
                {
                    try
                    {
                        var f = Releases.First(x => x.Name == dep);
                        f.Dependents.Add(release.Name);
                    } catch {}
                });
        }

        private void RenderModList()
        {
            listViewMods.Items.Clear();

            foreach (ReleaseInfo release in Releases)
            {
                if (searchBarText.Text != "" && !release.Name.ToLower().Contains(searchBarText.Text.ToLower()))
                    continue;

                ListViewItem item = new()
                {
                    Text = $"{release.Name} - {release.Version}",
                    Tag = release
                };
                item.SubItems.Add(release.Author);
                
                listViewMods.Items.Add(item);

                CheckDefaultMod(release, item);

                if (release.Group == null || !Groups.Contains(release.Group))
                {
                    item.Group = listViewMods.Groups[Groups.IndexOf("Uncategorized")];
                }
                else if (Groups.Contains(release.Group))
                {
                    var index = Groups.IndexOf(release.Group);
                    item.Group = listViewMods.Groups[index];
                }
                else
                {
                    var index = listViewMods.Groups.Add(new ListViewGroup(release.Group, HorizontalAlignment.Left));
                    item.Group = listViewMods.Groups[index];
                }
            }
        }

        private void LoadRequiredPlugins()
        {
            UpdateStatus("Getting latest version info...");
            LoadReleases();

            Invoke((MethodInvoker)(() =>
            {
                foreach (var group in Groups)
                    listViewMods.Groups.Add(new ListViewGroup(group, HorizontalAlignment.Left));
                

                RenderModList();

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

            foreach (var n in Releases.Where(r => r.Install))
                InstallMod(n);

            UpdateStatus("Install complete!");
            ChangeInstallButtonState(true);
        }

        void InstallMod(string modName)
        {
            var release = Releases.First(r => r.Name == modName);
            InstallMod(release);
        }

        void InstallMod(ReleaseInfo release)
        {
            UpdateStatus($"Downloading... {release.Name}");
            byte[] file = DownloadFile(release.Link);
            UpdateStatus($"Installing... {release.Name}");

            string fileName = Path.GetFileName(release.Link);
            if (Path.GetExtension(fileName).Equals(".dll"))
            {
                string dir = Path.Combine(InstallDirectory, "BepInEx", "plugins", release.Name);

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(Path.Combine(dir, fileName), file);
            }
            else
            {
                UnzipFile(file, InstallDirectory);
            }

            UpdateStatus($"Installed {release.Name}!");
        }

        #endregion // Installation

        #region UIEvents

        private void buttonInstall_Click(object sender, EventArgs e)
        {
            new Thread(Install).Start();
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
                        SetSavedLocation(InstallDirectory);
                    }
                    else
                    {
                        MessageBox.Show("That's not Gorilla Tag.exe! please try again!", "Error!", MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
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
                            if (Releases.Count(x => plugin.Dependents.Contains(x.Name) && x.Install) <= 1)
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
                if (Releases.Count(x => release.Dependents.Contains(x.Name) && x.Install) > 0)
                {
                    e.Item.Checked = true;
                }
            }

            if (release.Name == "BepInEx")
            {
                e.Item.Checked = true;
            }

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
            buttonModInfo.Enabled = (listViewMods.SelectedItems.Count > 0);
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

                var pluginsPath = Path.Combine(InstallDirectory, "BepInEx", "plugins");

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
                catch
                {
                    MessageBox.Show("Something went wrong!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateStatus("Failed to uninstall mods.");
                    return;
                }

                UpdateStatus("All mods uninstalled successfully!");
            }
        }

        private void backupMods()
        {
            var pluginsPath = Path.Combine(InstallDirectory, "BepInEx", "plugins");

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
                catch
                {
                    MessageBox.Show("Something went wrong!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateStatus("Failed to back up mods.");
                    return;
                }

                UpdateStatus("Successfully backed up mods!");
            }
        }

        private void buttonBackupMods_Click(object sender, EventArgs e)
        {
            backupMods();
        }

        private void restoreMods(bool bepinex = true)
        {
            using (var fileDialog = new OpenFileDialog())
            {
                fileDialog.InitialDirectory = InstallDirectory;
                fileDialog.FileName = "Mod Backup.zip";
                fileDialog.Filter = "ZIP Folder (.zip)|*.zip";
                fileDialog.FilterIndex = 1;
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (!Path.GetExtension(fileDialog.FileName)
                            .Equals(".zip", StringComparison.InvariantCultureIgnoreCase))
                    {
                        MessageBox.Show("Invalid file!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateStatus("Failed to restore mods.");
                        return;
                    }

                    var pluginsPath = Path.Combine(InstallDirectory, "BepInEx", "plugins");
                    try
                    {
                        UpdateStatus("Restoring mods...");
                        using (var archive = ZipFile.OpenRead(fileDialog.FileName))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                var directory = Path.Combine(pluginsPath, Path.GetDirectoryName(entry.FullName));
                                if (!Directory.Exists(directory))
                                {
                                    Directory.CreateDirectory(directory);
                                }

                                entry.ExtractToFile(Path.Combine(pluginsPath, entry.FullName), true);
                            }
                        }

                        UpdateStatus("Successfully restored mods!");
                    }
                    catch
                    {
                        MessageBox.Show("Something went wrong!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateStatus("Failed to restore mods.");
                    }
                }
            }
        }

        private void buttonRestoreMods_Click(object sender, EventArgs e)
        {
            restoreMods(true);
        }

        #region Folders

        private void buttonOpenGameFolder_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(InstallDirectory))
                Process.Start(InstallDirectory);
        }

        private void buttonOpenConfigFolder_Click(object sender, EventArgs e)
        {
            var bepConfig = Path.Combine(InstallDirectory, @"BepInEx\config");

            if (Directory.Exists(bepConfig))
                Process.Start(bepConfig);
            else
                MessageBox.Show(
                    $"You must install BepInEx and run the game before opening the config folder.",
                    "Missing Config Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void installModButton_Click(object sender, EventArgs e)
        {
            installModFromSystem();
        }

        private void searchBarText_TextChanged(object sender, EventArgs e) =>
            RenderModList();

        #endregion // UIEvents

        #region Helpers

        public static CookieContainer PermCookie;

        public static string DownloadSite(string URL)
        {
            try
            {
                if (PermCookie == null)
                {
                    PermCookie = new CookieContainer();
                }

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

        public static byte[] DownloadFile(string url)
        {
            var client = new WebClient();
            client.Proxy = null;
            return client.DownloadData(url);
        }

        private void UpdateStatus(string status)
        {
            string formattedText = string.Format("Status: {0}", status);
            this.Invoke((MethodInvoker)(() =>
            {
                //Invoke so we can call from any thread
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
                            MessageBox.Show("That's not Gorilla Tag.exe! please try again!", "Error!",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        Process.GetCurrentProcess().Kill();
                    }
                }
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
                UpdateStatus($"Opening source page for {release.Name}");
                
                if (!release.GitPath.Contains("https://") || !release.GitPath.Contains("http://"))
                    Process.Start(string.Format("https://github.com/{0}", release.GitPath));
                else
                    Process.Start(release.GitPath);
            }

        }

        #endregion // Helpers

        #region Registry

        private void LocationHandler()
        {
            var knownPath = GetSavedLocation() ?? GetSteamLocation();

            if (knownPath == "")
                knownPath = GetOculusLocation();

            if (knownPath != "" && Directory.Exists(knownPath) && File.Exists(knownPath + @"\Gorilla Tag.exe"))
            {
                textBoxDirectory.Text = knownPath;
                InstallDirectory = knownPath;
                PlatformDetected = true;
                return;
            }

            ShowErrorFindingDirectoryMessage();
        }

        private void ShowErrorFindingDirectoryMessage()
        {
            MessageBox.Show(
                "We couldn't seem to find your Gorilla Tag installation, please press \"OK\" and point us to it",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            NotFoundHandler();
            this.TopMost = true;
        }

        private string GetSteamLocation()
        {
            return (string)Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1533390",
                @"InstallLocation", "");
        }

        private string GetOculusLocation()
        {
            var oculusLibraryId = (string)Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Oculus VR, LLC\Oculus\Libraries",
                @"DefaultLibrary", "");

            if (string.IsNullOrEmpty(oculusLibraryId))
                return "";

            var oculusLibraryPath = (string)Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Oculus VR, LLC\Oculus\Libraries\" + oculusLibraryId,
                @"OriginalPath", "");

            if (string.IsNullOrEmpty(oculusLibraryPath))
                return "";

            var gtFolder = Path.Combine(oculusLibraryPath, "Software", "another-axiom-gorilla-tag");

            if (!Directory.Exists(gtFolder))
                return "";

            var gtExe = Path.Combine(gtFolder, "Gorilla Tag.exe");

            return File.Exists(gtExe) ? gtFolder : "";
        }

        private string GetSavedLocation()
        {
            return (string)Registry.GetValue(@"HKEY_CURRENT_USER\Software\SirKingBinx\MonkeModManager", @"InstallPath",
                "");
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

        private void installModFromSystem()
        {
            var pluginsPath = Path.Combine(InstallDirectory, "BepInEx", "plugins");

            using (var fileDialog = new OpenFileDialog())
            {
                fileDialog.Filter = "Plugins (*.dll)|*.dll|All Files (*.*)|*.*";
                fileDialog.FilterIndex = 1;

                if (fileDialog.ShowDialog() != DialogResult.OK)
                    return;
                
                var path = fileDialog.FileName;
                var friendlyName = fileDialog.SafeFileName ?? path;

                if (!Directory.Exists(pluginsPath)) Directory.CreateDirectory(pluginsPath);
                var dir = Path.Combine(pluginsPath, Regex.Replace(Path.GetFileNameWithoutExtension(friendlyName), @"\s+", string.Empty));

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllBytes(Path.Combine(dir, friendlyName), File.ReadAllBytes(path));
            }
        }

        #endregion
    }
}
