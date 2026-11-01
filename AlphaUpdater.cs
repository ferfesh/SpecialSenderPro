using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace AlphaTeamUpdater
{
    class Program
    {
        private const string GITHUB_RAW_BASE = "https://raw.githubusercontent.com/ferfesh/SpecialSenderPro/main/";
        private static string appDir;
        private static string logPath;
        private static bool isSilent = false;
        private static bool isForce = false;

        static void Main(string[] args)
        {
            try
            {
                appDir = AppDomain.CurrentDomain.BaseDirectory;
                logPath = Path.Combine(appDir, "updater.log");

                foreach (string arg in args)
                {
                    if (arg.Equals("-silent", StringComparison.OrdinalIgnoreCase) || arg.Equals("/silent", StringComparison.OrdinalIgnoreCase))
                    {
                        isSilent = true;
                    }
                    if (arg.Equals("-force", StringComparison.OrdinalIgnoreCase) || arg.Equals("/force", StringComparison.OrdinalIgnoreCase))
                    {
                        isForce = true;
                    }
                }

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

                Log("=================================================");
                Log("=== Starting Periodic Auto-Update Execution ===");
                Log("Mode: " + (isSilent ? "Silent Background" : "Interactive") + " | Force: " + isForce);

                // Self-heal scheduled task registration
                EnsureScheduledTask();

                // 1. Check 3-day interval rule
                if (!isForce && isSilent)
                {
                    if (!ShouldRunCheck())
                    {
                        Log("Periodic rule: Last check was recent (less than 3 days ago). Exiting silently.");
                        return;
                    }
                }

                // 2. Perform updates check
                PerformUpdateCycle();

                // 3. Mark check time
                UpdateLastCheckTimestamp();

                Log("=== Periodic Auto-Update Cycle Finished Successfully ===");
            }
            catch (Exception ex)
            {
                Log("Fatal Updater Error: " + ex.Message);
                if (!isSilent)
                {
                    System.Windows.Forms.MessageBox.Show("خطأ أثناء التحديث التلقائي: " + ex.Message, "Alpha Team Updater", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        private static void Log(string message)
        {
            try
            {
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine;
                File.AppendAllText(logPath, line);
            }
            catch { }
        }

        private static bool ShouldRunCheck()
        {
            try
            {
                string confPath = Path.Combine(appDir, "launcher.conf");
                if (!File.Exists(confPath)) return true;

                string[] lines = File.ReadAllLines(confPath);
                foreach (string line in lines)
                {
                    string t = line.Trim();
                    if (t.StartsWith("LAST_UPDATE_CHECK=", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = t.Substring(18).Trim();
                        DateTime lastDate;
                        if (DateTime.TryParse(val, out lastDate))
                        {
                            TimeSpan diff = DateTime.Now - lastDate;
                            if (diff.TotalDays < 3.0)
                            {
                                return false; // less than 3 days
                            }
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        private static void UpdateLastCheckTimestamp()
        {
            try
            {
                string confPath = Path.Combine(appDir, "launcher.conf");
                string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (File.Exists(confPath))
                {
                    string[] lines = File.ReadAllLines(confPath);
                    bool found = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Trim().StartsWith("LAST_UPDATE_CHECK=", StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = "LAST_UPDATE_CHECK=" + nowStr;
                            found = true;
                            break;
                        }
                    }
                    if (found)
                    {
                        File.WriteAllLines(confPath, lines);
                        return;
                    }
                }
                File.AppendAllText(confPath, "\r\nLAST_UPDATE_CHECK=" + nowStr + "\r\n");
            }
            catch { }
        }

        private static void PerformUpdateCycle()
        {
            Log("Connecting directly to official vendor update feed (https://launcher.senderprov.com/Updater/Updater.txt)...");
            try
            {
                using (WebClient wc = new WebClient())
                {
                    string txt = wc.DownloadString("https://launcher.senderprov.com/Updater/Updater.txt");
                    if (!string.IsNullOrEmpty(txt) && txt.Contains("[Marketing]"))
                    {
                        Log("Official vendor update feed received successfully! Checking tools versions...");
                        CheckAndDownloadFromVendorFeed(txt);
                        return;
                    }
                }
            }
            catch (Exception exVendor)
            {
                Log("Warning: Could not connect to official vendor feed: " + exVendor.Message);
            }
        }

        private static void CheckAndDownloadFromVendorFeed(string feedText)
        {
            try
            {
                string[] lines = feedText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string currentSection = "";
                string marketingLink = "", marketingVer = "";
                string dataLink = "", dataVer = "";
                string analysisLink = "", analysisVer = "";

                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line.Substring(1, line.Length - 2).ToLower();
                    }
                    else if (line.IndexOf('=') > 0)
                    {
                        string[] parts = line.Split(new char[] { '=' }, 2);
                        string k = parts[0].Trim().ToLower();
                        string v = parts[1].Trim();
                        if (k == "link")
                        {
                            if (currentSection == "marketing") marketingLink = v;
                            else if (currentSection == "data" || currentSection == "c2c") dataLink = v;
                            else if (currentSection == "dataanalysis") analysisLink = v;
                        }
                        else if (k == "version")
                        {
                            if (currentSection == "marketing") marketingVer = v;
                            else if (currentSection == "data" || currentSection == "c2c") dataVer = v;
                            else if (currentSection == "dataanalysis") analysisVer = v;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(marketingLink))
                    DownloadDirectToolZip("Sender Pro Marketing", "Sender Pro.exe", marketingLink, marketingVer);
                if (!string.IsNullOrEmpty(dataLink))
                    DownloadDirectToolZip("Sender Pro Data", "Sender Pro.exe", dataLink, dataVer);
                if (!string.IsNullOrEmpty(analysisLink))
                    DownloadDirectToolZip("Sender Pro DataAnalysis", "Sender Pro Data Analysis.exe", analysisLink, analysisVer);
            }
            catch (Exception ex)
            {
                Log("Vendor feed parse error: " + ex.Message);
            }
        }

        private static string ResolveToolFolder(string folderName)
        {
            string p1 = Path.Combine(appDir, folderName);
            if (Directory.Exists(p1)) return p1;

            DirectoryInfo pInfo = Directory.GetParent(appDir);
            string parent = pInfo != null ? pInfo.FullName : null;
            if (!string.IsNullOrEmpty(parent))
            {
                string p2 = Path.Combine(parent, folderName);
                if (Directory.Exists(p2)) return p2;

                DirectoryInfo gpInfo = Directory.GetParent(parent);
                string grandParent = gpInfo != null ? gpInfo.FullName : null;
                if (!string.IsNullOrEmpty(grandParent))
                {
                    string p3 = Path.Combine(grandParent, folderName);
                    if (Directory.Exists(p3)) return p3;
                }
            }
            return p1;
        }

        private static void DownloadDirectToolZip(string folderName, string exeName, string directUrl, string remoteVersionStr)
        {
            try
            {
                string targetFolder = ResolveToolFolder(folderName);
                string toolExe = Path.Combine(targetFolder, exeName);

                bool needsUpdate = false;
                if (!Directory.Exists(targetFolder) || !File.Exists(toolExe))
                {
                    needsUpdate = true;
                    Log("Tool missing or incomplete: " + folderName + ". Starting download from official vendor CDN...");
                }
                else if (!string.IsNullOrEmpty(remoteVersionStr))
                {
                    try
                    {
                        FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(toolExe);
                        Version localVer = new Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart);
                        Version remoteVer = new Version(remoteVersionStr);
                        if (remoteVer > localVer)
                        {
                            needsUpdate = true;
                            Log("New official vendor version available for " + folderName + ": " + remoteVer + " (Local: " + localVer + "). Updating...");
                        }
                        else
                        {
                            Log(folderName + " is up to date (Version " + localVer + ").");
                        }
                    }
                    catch
                    {
                        // Fallback
                    }
                }

                if (needsUpdate)
                {
                    string tempZip = Path.Combine(appDir, folderName + "_official_auto.zip");
                    using (WebClient wc = new WebClient())
                    {
                        wc.DownloadFile(directUrl, tempZip);
                    }
                    if (File.Exists(tempZip) && new FileInfo(tempZip).Length > 1000)
                    {
                        Log("Extracting official package for " + folderName + "...");
                        if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                        using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                        {
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                string destPath = Path.Combine(targetFolder, entry.FullName);
                                string destDir = Path.GetDirectoryName(destPath);
                                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                                if (!string.IsNullOrEmpty(entry.Name))
                                {
                                    entry.ExtractToFile(destPath, true);
                                }
                            }
                        }
                        File.Delete(tempZip);
                        Log("Successfully installed/updated " + folderName + " directly from official vendor CDN.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("DownloadDirectToolZip [" + folderName + "] Error: " + ex.Message);
            }
        }



        private static void SyncFileFromGitHub(string fileName)
        {
            try
            {
                string target = Path.Combine(appDir, fileName);
                if (!File.Exists(target))
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.DownloadFile(GITHUB_RAW_BASE + fileName, target);
                    }
                    Log("Synced missing file from GitHub: " + fileName);
                }
            }
            catch { }
        }

        private static void InstallCertToCurrentUser(string certPath)
        {
            try
            {
                if (File.Exists(certPath))
                {
                    X509Certificate2 cert = new X509Certificate2(certPath);
                    X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadWrite);
                    if (!store.Certificates.Contains(cert))
                    {
                        store.Add(cert);
                        Log("Installed root certificate into CurrentUser store.");
                    }
                    store.Close();
                }
            }
            catch { }
        }

        private static string GetServerIp()
        {
            try
            {
                string confPath = Path.Combine(appDir, "server.conf");
                if (File.Exists(confPath))
                {
                    string[] lines = File.ReadAllLines(confPath);
                    foreach (string line in lines)
                    {
                        string t = line.Trim();
                        if (t.StartsWith("SERVER_IP=", StringComparison.OrdinalIgnoreCase))
                        {
                            return t.Substring(10).Trim();
                        }
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private static void EnsureScheduledTask()
        {
            try
            {
                System.Threading.ThreadPool.QueueUserWorkItem((state) =>
                {
                    try
                    {
                        string updaterExe = Path.Combine(appDir, "AlphaUpdater.exe");
                        if (!File.Exists(updaterExe)) return;

                        string taskName = "AlphaTeam-SenderPro-AutoUpdate";
                        string cmd = "/create /tn \"" + taskName + "\" /tr \"\\\"" + updaterExe + "\\\" -silent\" /sc daily /mo 3 /f";

                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = cmd,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        Process p = Process.Start(psi);
                        if (p != null) p.WaitForExit(3000);

                        RegistryKey runKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                        if (runKey != null)
                        {
                            runKey.SetValue("AlphaTeamSenderProUpdater", "\"" + updaterExe + "\" -silent");
                            runKey.Close();
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }
    }
}
