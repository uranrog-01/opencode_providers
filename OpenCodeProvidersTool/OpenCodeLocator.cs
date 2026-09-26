using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenCodeProvidersTool
{
    /// <summary>An OpenCode installation found on this machine.</summary>
    public class InstallInfo
    {
        public string Kind = "";
        public string Directory = "";
        public string Launcher = "";
        public string Version = "";

        public override string ToString()
        {
            return Kind + (Version.Length > 0 ? " " + Version : "");
        }
    }

    /// <summary>A candidate opencode config file found on disk.</summary>
    public class ConfigInfo
    {
        public string Path = "";
        public string Kind = "";
        public bool IsGlobal;
        public long Size;
        public DateTime Modified;
        public int ProviderCount = -1;
        public int ModelCount = -1;

        public string FileName
        {
            get { return System.IO.Path.GetFileName(Path); }
        }

        public override string ToString()
        {
            return Path;
        }
    }

    /// <summary>
    /// Locates the OpenCode installation and every config file it could be using,
    /// so the editor can open the right file without the user hunting for it.
    /// </summary>
    public static class OpenCodeLocator
    {
        private static string Home
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        }

        private static string Roaming
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }
        }

        private static string Local
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        private static bool DirExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && Directory.Exists(path); }
            catch { return false; }
        }

        private static bool FileExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path); }
            catch { return false; }
        }

        // ---------------------------------------------------------------- installs

        /// <summary>Every OpenCode installation we can identify, best guess first.</summary>
        public static List<InstallInfo> FindInstallations()
        {
            var found = new List<InstallInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string, string> add = (kind, directory) =>
            {
                if (!DirExists(directory)) return;
                string full;
                try { full = System.IO.Path.GetFullPath(directory); }
                catch { return; }
                if (!seen.Add(full)) return;
                found.Add(new InstallInfo
                {
                    Kind = kind,
                    Directory = full,
                    Version = ReadVersion(full).Length > 0 ? ReadVersion(full) : ReadExeVersion(full)
                });
            };

            // Launchers visible on PATH win, because that is what the user actually runs.
            foreach (string launcher in FindOnPath())
            {
                string directory = System.IO.Path.GetDirectoryName(launcher);
                string kind = "PATH";
                if (launcher.IndexOf("npm", StringComparison.OrdinalIgnoreCase) >= 0) kind = "npm global";

                // An npm shim points at the real package; report that directory instead.
                string npmRoot = System.IO.Path.Combine(Roaming, "npm", "node_modules");
                string package = ResolveNpmPackage(npmRoot);
                if (package != null && launcher.IndexOf("npm", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    add("npm global", package);
                }
                else
                {
                    add(kind, directory);
                }
            }

            add("npm global", ResolveNpmPackage(System.IO.Path.Combine(Roaming, "npm", "node_modules")));
            add("bun global", ResolveNpmPackage(System.IO.Path.Combine(Home, ".bun", "install", "global", "node_modules")));
            add("pnpm global", ResolveNpmPackage(System.IO.Path.Combine(Local, "pnpm", "global", "5", "node_modules")));
            add("bun", System.IO.Path.Combine(Home, ".bun", "bin"));
            add("scoop", System.IO.Path.Combine(Home, "scoop", "apps", "opencode", "current", "bin"));
            add("chocolatey", @"C:\ProgramData\chocolatey\bin");
            add("winget", System.IO.Path.Combine(Local, "Microsoft", "WinGet", "Links"));
            add("local install", System.IO.Path.Combine(Local, "Programs", "opencode"));
            add("local install", System.IO.Path.Combine(Local, "opencode"));
            add("program files", @"C:\Program Files\opencode");
            add("user bin", System.IO.Path.Combine(Home, ".local", "bin"));
            add("user bin", System.IO.Path.Combine(Home, ".opencode", "bin"));
            add("data dir", System.IO.Path.Combine(Home, ".local", "share", "opencode"));

            // The Electron Desktop app: electron-builder oneClick installs under
            // %LOCALAPPDATA%\Programs\<dir>. The appId sanitises to "@opencode-aidesktop"
            // on official installs; channel builds use "OpenCode Beta" / "OpenCode Dev".
            add("desktop app", System.IO.Path.Combine(Local, "Programs", "@opencode-aidesktop"));
            add("desktop app", System.IO.Path.Combine(Local, "Programs", "OpenCode"));
            add("desktop app", System.IO.Path.Combine(Local, "Programs", "OpenCode Beta"));
            add("desktop app", System.IO.Path.Combine(Local, "Programs", "OpenCode Dev"));
            add("desktop app", System.IO.Path.Combine(Local, "Programs", "ZCode"));

            // winget keeps versioned package folders; look for an opencode one.
            string wingetPackages = System.IO.Path.Combine(Local, "Microsoft", "WinGet", "Packages");
            if (DirExists(wingetPackages))
            {
                try
                {
                    foreach (string dir in Directory.GetDirectories(wingetPackages, "*opencode*"))
                    {
                        add("winget", dir);
                    }
                }
                catch { }
            }

            return found;
        }

        /// <summary>Finds opencode launchers by walking PATH.</summary>
        private static List<string> FindOnPath()
        {
            var launchers = new List<string>();
            string path;
            try { path = Environment.GetEnvironmentVariable("PATH") ?? ""; }
            catch { return launchers; }

            string[] extensions = { ".exe", ".cmd", ".bat", ".ps1", "" };
            foreach (string raw in path.Split(';'))
            {
                string directory = raw.Trim().Trim('"');
                if (directory.Length == 0 || !DirExists(directory)) continue;

                foreach (string extension in extensions)
                {
                    string candidate = System.IO.Path.Combine(directory, "opencode" + extension);
                    if (FileExists(candidate))
                    {
                        launchers.Add(candidate);
                        break;
                    }
                }
            }
            return launchers;
        }

        /// <summary>Returns the real package directory for opencode inside an npm-style node_modules root.</summary>
        private static string ResolveNpmPackage(string nodeModules)
        {
            if (!DirExists(nodeModules)) return null;
            foreach (string name in new[] { "opencode-ai", "opencode" })
            {
                string candidate = System.IO.Path.Combine(nodeModules, name);
                if (DirExists(candidate)) return candidate;
            }
            return null;
        }

        private static string ReadVersion(string directory)
        {
            try
            {
                string manifest = System.IO.Path.Combine(directory, "package.json");
                if (!File.Exists(manifest)) return "";
                JObject json = JObject.Parse(File.ReadAllText(manifest));
                string version = (string)json["version"];
                string name = (string)json["name"];
                if (version == null) return "";
                if (name != null && name.IndexOf("opencode", StringComparison.OrdinalIgnoreCase) < 0) return "";
                return version;
            }
            catch { return ""; }
        }

        /// <summary>
        /// Electron installs carry no package.json at the root; the version lives in
        /// the exe metadata. Picks the OpenCode / ZCode exe in the folder.
        /// </summary>
        private static string ReadExeVersion(string directory)
        {
            try
            {
                foreach (string exe in Directory.GetFiles(directory, "*.exe"))
                {
                    string name = System.IO.Path.GetFileName(exe);
                    if (name.IndexOf("opencode", StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("zcode", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(exe);
                    string version = info.ProductVersion;
                    if (string.IsNullOrEmpty(version)) version = info.FileVersion;
                    if (!string.IsNullOrEmpty(version)) return version.Trim();
                }
            }
            catch { }
            return "";
        }

        /// <summary>The OpenCode / ZCode exe inside a desktop-app install directory.</summary>
        public static string DesktopExe(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !DirExists(directory)) return null;
            try
            {
                string[] names = { "OpenCode.exe", "ZCode.exe", "OpenCode Beta.exe", "OpenCode Dev.exe" };
                foreach (string name in names)
                {
                    string candidate = System.IO.Path.Combine(directory, name);
                    if (FileExists(candidate)) return candidate;
                }
            }
            catch { }
            return null;
        }

        // ----------------------------------------------------------------- configs

        /// <summary>
        /// Every config file worth offering, in precedence order. The global config
        /// OpenCode loads by default is marked <see cref="ConfigInfo.IsGlobal"/>.
        /// </summary>
        public static List<ConfigInfo> FindConfigs()
        {
            var results = new List<ConfigInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string xdg = null;
            try { xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"); }
            catch { }

            // Global config first: this is the file OpenCode reads for the whole machine.
            if (!string.IsNullOrEmpty(xdg))
            {
                AddPair(results, seen, System.IO.Path.Combine(xdg, "opencode"), "Global (XDG_CONFIG_HOME)", true);
            }
            AddPair(results, seen, System.IO.Path.Combine(Home, ".config", "opencode"), "Global config", true);
            AddPair(results, seen, System.IO.Path.Combine(Home, ".opencode"), "Legacy ~/.opencode", false);
            AddPair(results, seen, System.IO.Path.Combine(Roaming, "opencode"), "Roaming AppData", false);
            AddPair(results, seen, System.IO.Path.Combine(Local, "opencode"), "Local AppData", false);
            AddPair(results, seen, System.IO.Path.Combine(Home, ".local", "share", "opencode"), "XDG data dir", false);
            AddPair(results, seen, AppDomain.CurrentDomain.BaseDirectory, "Portable (next to this exe)", false);

            // Anything else sitting in a dedicated opencode config directory.
            AddDirectoryScan(results, seen, System.IO.Path.Combine(Home, ".config", "opencode"), "Global config");

            // Project-local configs, walking up from where the app was started.
            string directory = SafeCurrentDirectory();
            for (int depth = 0; depth < 4 && !string.IsNullOrEmpty(directory); depth++)
            {
                AddPair(results, seen, directory, depth == 0 ? "Current folder" : "Project folder", false);
                try
                {
                    DirectoryInfo parent = Directory.GetParent(directory);
                    directory = parent == null ? null : parent.FullName;
                }
                catch { break; }
            }

            // Configs living inside a detected installation.
            foreach (InstallInfo install in FindInstallations())
            {
                AddPair(results, seen, install.Directory, install.Kind + " install", false);
                AddDirectoryScan(results, seen, install.Directory, install.Kind + " install");
            }

            foreach (ConfigInfo info in results) Measure(info);
            return results;
        }

        private static string SafeCurrentDirectory()
        {
            try { return Directory.GetCurrentDirectory(); }
            catch { return null; }
        }

        private static void AddPair(List<ConfigInfo> results, HashSet<string> seen, string directory, string kind, bool isGlobal)
        {
            if (string.IsNullOrEmpty(directory)) return;
            AddConfig(results, seen, System.IO.Path.Combine(directory, "opencode.jsonc"), kind, isGlobal);
            AddConfig(results, seen, System.IO.Path.Combine(directory, "opencode.json"), kind, isGlobal);
        }

        private static void AddDirectoryScan(List<ConfigInfo> results, HashSet<string> seen, string directory, string kind)
        {
            if (!DirExists(directory)) return;
            try
            {
                foreach (string file in Directory.GetFiles(directory, "*.jsonc"))
                {
                    if (LooksLikeConfig(file)) AddConfig(results, seen, file, kind, false);
                }
                foreach (string file in Directory.GetFiles(directory, "*.json"))
                {
                    if (LooksLikeConfig(file)) AddConfig(results, seen, file, kind, false);
                }
            }
            catch { }
        }

        /// <summary>
        /// Keeps package.json, lock files and other unrelated JSON out of the picker:
        /// a file qualifies only if it is named opencode* or actually has a provider block.
        /// </summary>
        private static bool LooksLikeConfig(string path)
        {
            string name = System.IO.Path.GetFileName(path);
            if (name.StartsWith("opencode", StringComparison.OrdinalIgnoreCase)) return true;

            try
            {
                var file = new FileInfo(path);
                if (file.Length > 8 * 1024 * 1024) return false;
                JObject root = JObject.Parse(File.ReadAllText(path));
                return root["provider"] is JObject;
            }
            catch { return false; }
        }

        private static void AddConfig(List<ConfigInfo> results, HashSet<string> seen, string path, string kind, bool isGlobal)
        {
            if (!FileExists(path)) return;

            string full;
            try { full = System.IO.Path.GetFullPath(path); }
            catch { return; }
            if (!seen.Add(full)) return;

            var info = new ConfigInfo { Path = full, Kind = kind, IsGlobal = isGlobal };
            try
            {
                var file = new FileInfo(full);
                info.Size = file.Length;
                info.Modified = file.LastWriteTime;
            }
            catch { }
            results.Add(info);
        }

        /// <summary>Counts providers and models so the picker can show something useful.</summary>
        private static void Measure(ConfigInfo info)
        {
            try
            {
                if (info.Size > 8 * 1024 * 1024) return;

                JObject root = JObject.Parse(File.ReadAllText(info.Path));
                JObject providers = root["provider"] as JObject;
                if (providers == null)
                {
                    info.ProviderCount = 0;
                    info.ModelCount = 0;
                    return;
                }

                info.ProviderCount = providers.Count;
                int models = 0;
                foreach (JProperty provider in providers.Properties())
                {
                    JObject block = provider.Value as JObject;
                    JObject modelMap = block == null ? null : block["models"] as JObject;
                    if (modelMap != null) models += modelMap.Count;
                }
                info.ModelCount = models;
            }
            catch
            {
                info.ProviderCount = -1;
                info.ModelCount = -1;
            }
        }

        /// <summary>The config OpenCode itself would load, or null when there is none.</summary>
        public static ConfigInfo FindActiveConfig(List<ConfigInfo> configs)
        {
            return configs.FirstOrDefault(c => c.IsGlobal);
        }

        /// <summary>One-line summary of where OpenCode lives, for the status bar.</summary>
        public static string DescribeInstallations(List<InstallInfo> installs)
        {
            if (installs.Count == 0) return "not found on PATH or in the usual install locations";
            InstallInfo best = installs[0];
            string text = best.Kind + (best.Version.Length > 0 ? " v" + best.Version : "");
            if (installs.Count > 1) text += "  (+" + (installs.Count - 1) + " more)";
            return text;
        }
    }
}
