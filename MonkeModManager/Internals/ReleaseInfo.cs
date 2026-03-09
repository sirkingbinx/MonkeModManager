using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MonkeModManager.Internals.SimpleJSON;

namespace MonkeModManager.Internals
{
    public class ReleaseInfo
    {
        public string Version;
        public string Link;
        public string Name;
        public string Author;
        public string GitPath;
        public string Group;
        public bool Install = true;
        public bool MelonLoader;
        public string MLInstallPath;

        public List<string> Dependencies = new List<string>();
        public List<string> Dependents = new List<string>();
        public ReleaseInfo(string _name, string _author, string _gitPath, string _version, string _group, string _downloadUrl, string _whereML, JSONArray dependencies)
        {
            Name = _name;
            Author = _author;
            GitPath = _gitPath;
            Version = _version;
            Group = _group;
            Link = _downloadUrl;
            MelonLoader = _whereML != null;
            MLInstallPath = _whereML;

            //if (MelonLoader)
               // Dependencies.Add("BepInEx.MelonLoader.Loader");

            if (dependencies == null) return;
            for (int i = 0; i < dependencies.Count; i++)
            {
                Dependencies.Add(dependencies[i]);
            }
        }
    }
}
