// GIF Live Wallpaper - entry point, settings, single instance and sign-in entry.
//
//   GifWallpaper.exe              open the control panel (and play the saved GIF)
//   GifWallpaper.exe --background play the saved GIF without the panel (used at sign-in)
//   GifWallpaper.exe --stop       stop a running copy
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("GIF Live Wallpaper")]
[assembly: AssemblyProduct("GIF Live Wallpaper")]
[assembly: AssemblyDescription("Animated GIF wallpaper behind your desktop icons")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("3.1.0.0")]
[assembly: AssemblyFileVersion("3.1.0.0")]
// Opts WPF into current .NET 4.8 behaviour (e.g. per-monitor DPI scaling of the panel).
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace GifWp
{
    public sealed class Settings
    {
        public string GifPath = "";
        public string MonitorMode = "Primary";   // Primary | Span | Each
        public string Fit = "Fill";              // Fill | Fit | Stretch | Center
        public int MaxFps = 30;
        public string Background = "#000000";
        public bool PauseWhenCovered = true;
        public bool PauseOnLowBattery = true;     // unplugged and <= 20 % or Battery saver on
        public bool LowPriority = true;
        public bool UseChildStyle = true;
        public string Memory = "Auto";            // Auto | Speed (lowest CPU) | Saver (lowest RAM)
        public bool AskedAutoStart = false;

        public Settings Clone() { return (Settings)MemberwiseClone(); }

        // The settings file is a flat JSON object, so a tiny reader/writer is enough.
        public static Settings Load(string path)
        {
            Settings s = new Settings();
            string json;
            try { json = File.ReadAllText(path); } catch { return s; }
            foreach (Match m in Regex.Matches(json, @"""(\w+)""\s*:\s*(""(?:[^""\\]|\\.)*""|[^,}\s]+)"))
            {
                string v = m.Groups[2].Value;
                if (v.StartsWith("\"")) v = Unescape(v.Substring(1, v.Length - 2));
                switch (m.Groups[1].Value)
                {
                    case "GifPath":          s.GifPath = v; break;
                    case "MonitorMode":      if (v == "Primary" || v == "Span" || v == "Each") s.MonitorMode = v; break;
                    case "Fit":              if (v == "Fill" || v == "Fit" || v == "Stretch" || v == "Center") s.Fit = v; break;
                    case "MaxFps":           s.MaxFps = Int(v, 30, 1, 60); break;
                    case "Background":       if (Regex.IsMatch(v, "^#[0-9A-Fa-f]{6}$")) s.Background = v; break;
                    case "PauseWhenCovered": s.PauseWhenCovered = Bool(v, true); break;
                    case "PauseOnLowBattery": s.PauseOnLowBattery = Bool(v, true); break;
                    case "LowPriority":      s.LowPriority = Bool(v, true); break;
                    case "UseChildStyle":    s.UseChildStyle = Bool(v, true); break;
                    case "Memory":           if (v == "Auto" || v == "Speed" || v == "Saver") s.Memory = v; break;
                    case "AskedAutoStart":   s.AskedAutoStart = Bool(v, false); break;
                }
            }
            return s;
        }

        public void Save(string path)
        {
            string json = "{\r\n" +
                "  \"GifPath\": " + J(GifPath) + ",\r\n" +
                "  \"MonitorMode\": " + J(MonitorMode) + ",\r\n" +
                "  \"Fit\": " + J(Fit) + ",\r\n" +
                "  \"MaxFps\": " + MaxFps + ",\r\n" +
                "  \"Background\": " + J(Background) + ",\r\n" +
                "  \"PauseWhenCovered\": " + B(PauseWhenCovered) + ",\r\n" +
                "  \"PauseOnLowBattery\": " + B(PauseOnLowBattery) + ",\r\n" +
                "  \"Memory\": " + J(Memory) + ",\r\n" +
                "  \"LowPriority\": " + B(LowPriority) + ",\r\n" +
                "  \"UseChildStyle\": " + B(UseChildStyle) + ",\r\n" +
                "  \"AskedAutoStart\": " + B(AskedAutoStart) + "\r\n}\r\n";
            WriteHidden(path, json, false);
        }

        // Settings and log are hidden files, so the folder shows only the exe.
        // (Overwriting a hidden file with FileMode.Create fails, hence Truncate.)
        public static void WriteHidden(string path, string text, bool append)
        {
            bool exists = File.Exists(path);
            FileMode mode = append ? FileMode.Append : exists ? FileMode.Truncate : FileMode.CreateNew;
            using (FileStream fs = new FileStream(path, mode, FileAccess.Write, FileShare.Read))
            {
                byte[] b = new UTF8Encoding(false).GetBytes(text);
                fs.Write(b, 0, b.Length);
            }
            try
            {
                FileAttributes a = File.GetAttributes(path);
                if ((a & FileAttributes.Hidden) == 0) File.SetAttributes(path, a | FileAttributes.Hidden);
            }
            catch { }
        }

        static string B(bool b) { return b ? "true" : "false"; }

        static string J(string v)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in v ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static int Int(string v, int def, int min, int max)
        {
            int n;
            if (!int.TryParse(v, out n)) n = def;
            return Math.Max(min, Math.Min(max, n));
        }

        static bool Bool(string v, bool def)
        {
            v = v.Trim().ToLowerInvariant();
            if (v == "true" || v == "1" || v == "yes" || v == "on") return true;
            if (v == "false" || v == "0" || v == "no" || v == "off") return false;
            return def;
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        break;
                    default: sb.Append(n); break;   // \" \\ \/
                }
            }
            return sb.ToString();
        }
    }

    // Portable: settings live next to the exe. Only if that folder is read-only
    // (e.g. Program Files) does it fall back to %APPDATA%\GifWallpaper.
    public static class AppPaths
    {
        public static string Exe, ExeDir, DataDir, Config, Log;

        public static void Init()
        {
            Exe = Assembly.GetEntryAssembly().Location;
            ExeDir = Path.GetDirectoryName(Exe);
            DataDir = ExeDir;
            if (!File.Exists(Path.Combine(ExeDir, "GifWallpaper.json")) && !CanWrite(ExeDir))
            {
                DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GifWallpaper");
                Directory.CreateDirectory(DataDir);
            }
            Config = Path.Combine(DataDir, "GifWallpaper.json");
            Log = Path.Combine(DataDir, "GifWallpaper.log");
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string t = Path.Combine(dir, ".gifwp-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(t, "");
                File.Delete(t);
                return true;
            }
            catch { return false; }
        }
    }

    // The only thing the app ever writes outside its own folder (and only if you ask).
    public static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "GifWallpaper";

        static string Command { get { return "\"" + AppPaths.Exe + "\" --background"; } }

        static string Current()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                return k == null ? null : k.GetValue(Name) as string;
        }

        public static bool IsOn { get { return Current() == Command; } }

        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (on) k.SetValue(Name, Command);
                else if (k.GetValue(Name) as string == Command) k.DeleteValue(Name, false);   // only our own entry, never another copy's
            }
        }

        // If the exe was moved (its old path is gone) or an older script-based version set
        // the entry, point it here. Another working copy elsewhere is left alone.
        public static void Sync()
        {
            string cur = Current();
            if (cur == null || cur == Command) return;
            bool legacy = cur.IndexOf("GifWallpaper.ps1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          cur.IndexOf(@"\bin\GifWallpaper.exe", StringComparison.OrdinalIgnoreCase) >= 0;
            string target = cur.StartsWith("\"") && cur.IndexOf('"', 1) > 0 ? cur.Substring(1, cur.IndexOf('"', 1) - 1) : cur;
            if (legacy || !File.Exists(target)) Set(true);
        }
    }

    public static class Program
    {
        public const string Version = "3.1.0";
        const string MutexName = @"Local\GifLiveWallpaper", ShowName = @"Local\GifLiveWallpaper.Show", QuitName = @"Local\GifLiveWallpaper.Quit";

        public static Engine Engine;
        public static Settings Settings;
        static Application app;
        static Panel panel;

        [STAThread]
        public static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Already running: hand over to that copy and exit.
                    if (mode == "--stop") Signal(QuitName);
                    else if (mode != "--background") Signal(ShowName);
                    return 0;
                }
                if (mode == "--stop") return 0;
                try { return Run(mode == "--background"); }
                catch (Exception ex)
                {
                    try { Log("FATAL: " + ex); } catch { }
                    MessageBox.Show("GIF Live Wallpaper ran into a problem:\n\n" + ex.Message, "GIF Live Wallpaper", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
                finally { try { mutex.ReleaseMutex(); } catch { } }
            }
        }

        static int Run(bool background)
        {
            Signal(@"Local\GifWallpaper.Stop");   // stop an older (PowerShell-based) version if it is running
            AppPaths.Init();
            Settings = Settings.Load(AppPaths.Config);
            if (Settings.LowPriority) Native.SetLowPower();
            try   // settings from an older version: rewrite once in the current format, as a hidden file
            {
                if (File.Exists(AppPaths.Config) && (File.GetAttributes(AppPaths.Config) & FileAttributes.Hidden) == 0) SaveSettings();
            }
            catch { }

            bool haveGif = File.Exists(Settings.GifPath);
            if (background && !haveGif) return 0;

            app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Engine = new Engine(Log);
            Engine.StatusChanged += delegate { if (panel != null) panel.UpdateStatus(); ExitIfIdle(); };

            EventWaitHandle showEv = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
            EventWaitHandle quitEv = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);
            ThreadPool.RegisterWaitForSingleObject(showEv, delegate { app.Dispatcher.BeginInvoke(new Action(ShowPanel)); }, null, -1, false);
            ThreadPool.RegisterWaitForSingleObject(quitEv, delegate { app.Dispatcher.BeginInvoke(new Action(Quit)); }, null, -1, false);

            app.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (haveGif) Engine.Start(Settings);
                if (!background) { try { AutoStart.Sync(); } catch { } ShowPanel(); }
                ExitIfIdle();
            }));
            app.Run();
            return 0;
        }

        static void Signal(string name)
        {
            try { using (EventWaitHandle e = EventWaitHandle.OpenExisting(name)) e.Set(); } catch { }
        }

        public static void ShowPanel()
        {
            if (panel == null)
            {
                panel = new Panel();
                panel.Closed += delegate { panel = null; ExitIfIdle(); if (Engine.Running) Native.Trim(); };
                panel.Show();
            }
            if (panel.WindowState == WindowState.Minimized) panel.WindowState = WindowState.Normal;
            panel.Activate();
        }

        // The process lives while the wallpaper plays or the panel is open.
        static void ExitIfIdle()
        {
            if (!exiting && panel == null && !Engine.Running) { exiting = true; app.Shutdown(); }
        }

        public static void Quit()
        {
            if (exiting) return;
            exiting = true;
            if (panel != null) panel.Close();
            Engine.Stop();
            app.Shutdown();
        }

        static bool exiting;

        public static void SaveSettings()
        {
            try { Settings.Save(AppPaths.Config); }
            catch (Exception ex) { Log("could not save settings: " + ex.Message); }
        }

        // Only problems are logged, so a healthy install never creates a log file.
        public static void Log(string m)
        {
            try
            {
                FileInfo fi = new FileInfo(AppPaths.Log);
                if (fi.Exists && fi.Length > 65536) fi.Delete();
                Settings.WriteHidden(AppPaths.Log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine, true);
            }
            catch { }
        }

        // "Remove from this PC": undo everything the app wrote, then quit.
        public static void RemoveTraces()
        {
            Engine.Stop();
            try { AutoStart.Set(false); } catch { }
            foreach (string f in new[] { AppPaths.Config, AppPaths.Log })
                try { if (File.Exists(f)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } } catch { }
            if (AppPaths.DataDir != AppPaths.ExeDir) try { Directory.Delete(AppPaths.DataDir, true); } catch { }
        }
    }
}
