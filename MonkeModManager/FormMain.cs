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

        private const string BaseEndpoint = "https://api.github.com/repos/";
        private const Int16 CurrentVersion = 4;
        private List<ReleaseInfo> releases;
        Dictionary<string, int> groups = new Dictionary<string, int>();
        private string InstallDirectory = @"";
        public bool isSteam = true;
        public bool platformDetected = false;
        public bool listOtherLoaderMods = false;

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
            labelVersion.Text = "MonkeModManager v" + version.Substring(0, version.Length - 2);

            new Thread(() =>
            {
                AutoDetectModLoader();
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
            
            JSONArray allMods;

            if (!listOtherLoaderMods)
            {
                allMods = decoded[IsBepInEx() ? "mods" : "melonloader_mods"].AsArray;
            }
            else
            {
                // put the tables together and remove duplicates from other mod loaders
                var allBepMods = decoded["mods"].AsArray;
                var allMLMods = decoded["melonloader_mods"].AsArray;

                allMods = new JSONArray();
                var safeTable = IsBepInEx() ? allBepMods : allMLMods;
                var movingTable = IsBepInEx() ? allMLMods : allBepMods;

                allMods = safeTable;
                foreach (var item in movingTable)
                {
                    if (IsBepInEx() && item.Value["name"] == "MelonLoader")
                        continue;
                    if (!IsBepInEx() && item.Value["name"] == "BepInEx")
                        continue;
                    if (item.Value["name"] == "MelInEx")
                        continue;

                    var exists = allMods.Linq.Select(e => e.Value["name"]).Contains(item.Value["name"]);
                    if (exists)
                        continue;

                    item.Value["name"] += " [MelInEx]";
                    allMods.Add(item.Value);
                }
            }

            
            var allGroups = decoded["groups"].AsArray;

            for (int i = 0; i < allMods.Count; i++)
            {
                JSONNode current = allMods[i];
                ReleaseInfo release = new ReleaseInfo(current["name"], current["author"], current["gitPath"],
                    current["version"], current["group"], current["browser_download_url"],
                    current["dependencies"].AsArray);

                release.MelInEx = release.Name.Contains("[MelInEx]");
                if (release.MelInEx)
                    release.Dependencies.Add("MelInEx");

                releases.Add(release);
            }

            for (int i = 0; i < allGroups.Count; i++)
            {
                JSONNode current = allGroups[i];
                if (releases.Any(x => x.Group == current["name"]) && !groups.ContainsKey(current["name"]))
                {
                    groups.Add(current["name"], groups.Count());
                }
            }

            if (!groups.ContainsKey("Uncategorized"))
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

        private void RenderModList()
        {
            listViewMods.Items.Clear();

            foreach (ReleaseInfo release in releases)
            {
                if (searchBarText.Text != "" && !release.Name.ToLower().Contains(searchBarText.Text.ToLower()))
                    continue;

                ListViewItem item = new ListViewItem();
                item.Text = release.Name;
                item.Text = $"{release.Name} - {release.Version}";
                item.SubItems.Add(release.Author);
                item.Tag = release;
                
                listViewMods.Items.Add(item);

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
                    int index = listViewMods.Groups.Add(new ListViewGroup(release.Group, HorizontalAlignment.Left));
                    item.Group = listViewMods.Groups[index];
                }
            }
        }

        private void LoadRequiredPlugins()
        {
            UpdateStatus("Getting latest version info...");
            LoadReleases();
            this.Invoke((MethodInvoker)(() =>
            {
                //Invoke so we can call from current thread
                //Update checkbox's text
                Dictionary<string, int> includedGroups = new Dictionary<string, int>();

                for (int i = 0; i < groups.Count(); i++)
                {
                    var key = groups.First(x => x.Value == i).Key;
                    var value = listViewMods.Groups.Add(new ListViewGroup(key, HorizontalAlignment.Left));
                    groups[key] = value;
                }

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

            foreach (var n in releases.Where(r => r.Install))
                InstallMod(n);

            UpdateStatus("Install complete!");
            ChangeInstallButtonState(true);
        }

        void InstallMod(string modName)
        {
            var release = releases.First(r => r.Name == modName);
            InstallMod(release);
        }

        void InstallMod(ReleaseInfo release)
        {
            UpdateStatus(string.Format("Downloading...{0}", release.Name));
            byte[] file = DownloadFile(release.Link);
            UpdateStatus(string.Format("Installing...{0}", release.Name));
            string fileName = Path.GetFileName(release.Link);
            if (Path.GetExtension(fileName).Equals(".dll"))
            {
                string dir = GetPluginsPath(release.MelInEx);

                if (dir.EndsWith(@"BepInEx\plugins"))
                    dir += $"\\{release.Name}";

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                dir = Path.Combine(dir);

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllBytes(Path.Combine(dir, fileName), file);
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
            new Thread(Install).Start();
        }

        private void restoreMelonLoaderButton_Click(object sender, EventArgs e)
        {
            restoreMods(false);
        }

        private void modLoaderBox_SelectionChangeCommitted(object sender, EventArgs e)
        {
            if (MessageBox.Show("Please backup your mods so you can restore them in case the transition fails.",
                    this.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
            {
                backupMods();
            }
            modLoaderAutoDetectBox.Checked = false;
            autoDetectedLabel.Visible = false;

            listViewMods.Items.Clear();
            listViewMods.Groups.Clear();

            Registry.SetValue(@"HKEY_CURRENT_USER\Software\SirKingBinx\MonkeModManager", "Loader", modLoaderBox.Text);

            LoadRequiredPlugins();
            InstallMod(modLoaderBox.Text);

            UpdateStatus("Restarting..");

            Application.Restart();
            Environment.Exit(0);
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
                        AutoDetectModLoader();
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

            if (release.Name == modLoaderBox.Text)
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

                var pluginsPath = GetPluginsPath();

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

        private void backupMods()
        {
            var pluginsPath = GetPluginsPath();

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

                    var pluginsPath = GetPluginsPath();
                    try
                    {
                        UpdateStatus("Restoring mods...");
                        using (var archive = ZipFile.OpenRead(fileDialog.FileName))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                var directory = Path.Combine(GetPluginsPath(), Path.GetDirectoryName(entry.FullName));
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
            var melonConfig = Path.Combine(InstallDirectory, @"UserData");

            if (modLoaderBox.Text == "BepInEx" && Directory.Exists(bepConfig))
                Process.Start(bepConfig);
            else if (modLoaderBox.Text == "MelonLoader" && Directory.Exists(melonConfig))
                Process.Start(melonConfig);
            else
                MessageBox.Show(
                    $"You must install and run {modLoaderBox.Text} before opening the config folder.",
                    "Missing Config Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void buttonOpenBepInExFolder_Click(object sender, EventArgs e)
        {
            if (modLoaderBox.Text != "BepInEx")
            {
                MessageBox.Show(
                    $"You're using {modLoaderBox.Text}.",
                    "Missing BepInEx", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

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
            installModFromSystem(true);
        }

        private void mlFolder_Click(object sender, EventArgs e)
        {
            if (modLoaderBox.Text == "MelonLoader" && Directory.Exists(Path.Combine(InstallDirectory, "MelonLoader")))
            {
                Process.Start(Path.Combine(InstallDirectory, "MelonLoader"));
                return;
            }
            if (modLoaderBox.Text == "BepInEx")
            {
                if (!Directory.Exists(Path.Combine(InstallDirectory, "MLLoader")))
                {
                    MessageBox.Show(
                        "Please install the MelonLoader to BepInEx compatability layer (BepInEx.MelonLoader.Loader.)",
                        "Missing Compatability Layer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Process.Start(Path.Combine(InstallDirectory, "MLLoader"));
            }
        }

        private void installMelonLoaderModButton_Click(object sender, EventArgs e) =>
            installModFromSystem(false);

        private void searchBarText_TextChanged(object sender, EventArgs e) =>
            RenderModList();

        #endregion // UIEvents

        #region Helpers

        private CookieContainer PermCookie;

        private string DownloadSite(string URL)
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
                        this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show("Failed to update version info, please check your internet connection", this.Text,
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

        private byte[] DownloadFile(string url)
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
                            AutoDetectModLoader();
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
                UpdateStatus($"Opening GitHub page for {release.Name}");
                Process.Start(string.Format("https://github.com/{0}", release.GitPath));
            }

        }

        private void modLoaderAutoDetectBox_CheckedChanged(object sender, EventArgs e)
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\SirKingBinx\MonkeModManager", "AutoDetectLoader",
                modLoaderAutoDetectBox.Visible ? "True" : "False");
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
            if (release.Name == modLoaderBox.Text)
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

        private void installModFromSystem(bool isBepInEx)
        {
            using (var fileDialog = new OpenFileDialog())
            {
                fileDialog.Filter = "Plugins (*.dll)|*.dll|All Files (*.*)|*.*";
                fileDialog.FilterIndex = 1;

                if (fileDialog.ShowDialog() != DialogResult.OK)
                    return;

                var crossLoaded = isBepInEx != (modLoaderBox.Text == "BepInEx");

                var pluginsPath = GetPluginsPath(crossLoaded);
                if (crossLoaded)
                    InstallMelInEx();
                
                var path = fileDialog.FileName;
                var friendlyName = fileDialog.SafeFileName ?? path;

                if (!Directory.Exists(pluginsPath)) Directory.CreateDirectory(pluginsPath);
                var dir = Path.Combine(pluginsPath,
                    isBepInEx
                        ? Regex.Replace(Path.GetFileNameWithoutExtension(friendlyName), @"\s+", string.Empty)
                        : "");

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllBytes(Path.Combine(dir, friendlyName), File.ReadAllBytes(path));
            }
        }

        #endregion

        #region ModLoader Utils

        public struct ModFolder
        {
            public string name;
            public bool dir;
        }

        public static ModFolder[] BepInExData = new ModFolder[]
        {
            new ModFolder{name="BepInEx", dir=true},
            new ModFolder{name="MLLoader", dir=true},
            new ModFolder{name=".doorstop_version", dir=false},
            new ModFolder{name="changelog.txt", dir=false},
            new ModFolder{name="doorstop_config.ini", dir=false},
            new ModFolder{name="winhttp.dll", dir=false},
        };

        public static ModFolder[] MelonLoaderData = new ModFolder[]
        {
            new ModFolder{name="MelonLoader", dir=true},
            new ModFolder{name="UserData", dir=true},
            new ModFolder{name="UserLibs", dir=true},
            new ModFolder{name="Plugins", dir=true},
            new ModFolder{name="Mods", dir=true},
            new ModFolder{name="version.dll", dir=false},
        };

        public void CleanModLoaderFiles()
        {
            // cleanup files if you are transitioning from another mod loader
            var cleanStuff = modLoaderBox.Text == "MelonLoader" ? BepInExData : MelonLoaderData;

            foreach (var data in cleanStuff)
            {
                var p = Path.Combine(InstallDirectory, data.name);
                if (data.dir && Directory.Exists(p))
                    Directory.Delete(p, true);
                else if (File.Exists(p))
                    File.Delete(p);
            }
        }

        private string GetPluginsPath(bool crossLoadedMod = false)
        {
            var bepInEx = (modLoaderBox.Text == "BepInEx");

            if (bepInEx && !crossLoadedMod)
                return Path.Combine(InstallDirectory, @"BepInEx\plugins");
            if (bepInEx)
                return Path.Combine(InstallDirectory, @"BepInEx\MelonLoader\Mods");
            if (!crossLoadedMod)
                return Path.Combine(InstallDirectory, @"Mods");

            return Path.Combine(InstallDirectory, @"BepInEx\plugins");
        }

        private bool IsBepInEx()
        {
            return modLoaderBox.Text == "BepInEx";
        }

        private void AutoDetectModLoader()
        {
            modLoaderAutoDetectBox.Checked =
                (string)Registry.GetValue(@"HKEY_CURRENT_USER\Software\SirKingBinx\MonkeModManager", "AutoDetectLoader",
                    "True") == "True";
            modLoaderBox.Text = (string)Registry.GetValue(@"HKEY_CURRENT_USER\Software\SirKingBinx\MonkeModManager", "Loader", "BepInEx");

            listOtherLoaderMods = modLoaderBox.Text == "BepInEx"
                ? File.Exists(Path.Combine(InstallDirectory, @"BepInEx\plugins\MelInEx\MelInEx.dll"))
                : File.Exists(Path.Combine(InstallDirectory, @"Mods\MelInEx.dll"));

            if (!modLoaderAutoDetectBox.Checked)
                return;

            bool bepinex = false;

            // with complayer
            if (Directory.Exists(Path.Combine(InstallDirectory, "BepInEx")) &&
                Directory.Exists(Path.Combine(InstallDirectory, "BepInEx", "MelonLoader")))
                bepinex = true;

            if (Directory.Exists(Path.Combine(InstallDirectory, "BepInEx")) &&
                Directory.Exists(Path.Combine(InstallDirectory, "MelonLoader")))
                bepinex = false;

            if (Directory.Exists(Path.Combine(InstallDirectory, "BepInEx")) &&
                !Directory.Exists(Path.Combine(InstallDirectory, "MelonLoader")))
                bepinex = true;

            if (Directory.Exists(Path.Combine(InstallDirectory, "MelonLoader")) &&
                !Directory.Exists(Path.Combine(InstallDirectory, "BepInEx")))
                bepinex = false;

            listOtherLoaderMods = bepinex
                ? File.Exists(Path.Combine(InstallDirectory, @"BepInEx\plugins\MelInEx\MelInEx.dll"))
                : File.Exists(Path.Combine(InstallDirectory, @"Mods\MelInEx.dll"));

            modLoaderBox.Text = bepinex ? "BepInEx" : "MelonLoader";
            autoDetectedLabel.Visible = true;
        }

        private void InstallMelInEx() =>
            InstallMod("MelInEx");

        #endregion
    }
}
