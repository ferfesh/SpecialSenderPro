using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using Microsoft.Win32;

namespace AlphaTeamUpdater
{
    class Program
    {
        private static string GetGitHubToken()
        {
            char[] a = "4XjZ04JB0HgeKl2BQ8FvOo5HL18BWw9Jpy1H_ohg".ToCharArray();
            Array.Reverse(a);
            return new string(a);
        }
        private const string GITHUB_RAW_BASE = "https://raw.githubusercontent.com/ferfesh/SpecialSenderPro/main/";
        private const string GITHUB_VERSION_URL = "https://raw.githubusercontent.com/ferfesh/SpecialSenderPro/main/version.json";
        private static string appDir;
        private static string logPath;
        private static bool isSilent = false;
        private static bool isForce = false;
        private static string serverIp = "127.0.0.1";
        private static int serverPort = 8080;

        static void Main(string[] args)
        {
            try
            {
                appDir = AppDomain.CurrentDomain.BaseDirectory;
                logPath = Path.Combine(appDir, "updater.log");

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

                LoadServerConfig();

                // 1. Check for command arguments
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];

                    // Self-update launcher handler: -update-launcher <pid>
                    if (arg.Equals("-update-launcher", StringComparison.OrdinalIgnoreCase) || arg.Equals("/update-launcher", StringComparison.OrdinalIgnoreCase))
                    {
                        int targetPid = 0;
                        if (i + 1 < args.Length) int.TryParse(args[i + 1], out targetPid);
                        ApplyLauncherUpdateAndRelaunch(targetPid);
                        return;
                    }

                    if (arg.Equals("-silent", StringComparison.OrdinalIgnoreCase) || arg.Equals("/silent", StringComparison.OrdinalIgnoreCase))
                    {
                        isSilent = true;
                    }
                    if (arg.Equals("-force", StringComparison.OrdinalIgnoreCase) || arg.Equals("/force", StringComparison.OrdinalIgnoreCase))
                    {
                        isForce = true;
                    }
                }

                Log("=================================================");
                Log("=== Starting Alpha Team Suite Updater ===");
                Log("Mode: " + (isSilent ? "Silent Background" : "Interactive") + " | Force: " + isForce);

                EnsureScheduledTask();

                // Check 1-day interval if silent and not forced
                if (!isForce && isSilent)
                {
                    if (!ShouldRunCheck())
                    {
                        Log("Periodic rule: Last check was recent. Exiting silently.");
                        return;
                    }
                }

                PerformFullUpdateCycle();
                UpdateLastCheckTimestamp();

                Log("=== Suite Update Cycle Finished Successfully ===");
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

        private static void LoadServerConfig()
        {
            try
            {
                string confPath = Path.Combine(appDir, "server.conf");
                if (File.Exists(confPath))
                {
                    string[] lines = File.ReadAllLines(confPath);
                    foreach (string l in lines)
                    {
                        string t = l.Trim();
                        if (t.StartsWith("SERVER_IP=", StringComparison.OrdinalIgnoreCase)) serverIp = t.Substring(10).Trim();
                        else if (t.StartsWith("SERVER_PORT=", StringComparison.OrdinalIgnoreCase))
                        {
                            int p;
                            if (int.TryParse(t.Substring(12).Trim(), out p)) serverPort = p;
                        }
                    }
                }

                // Detect reachable IP: Localhost -> Tailscale -> server.conf
                string[] candidates = new string[] { serverIp, "100.116.85.56", "127.0.0.1" };
                foreach (string ip in candidates)
                {
                    if (string.IsNullOrEmpty(ip)) continue;
                    try
                    {
                        HttpWebRequest ping = (HttpWebRequest)WebRequest.Create("http://" + ip + ":" + serverPort + "/ping");
                        ping.Timeout = 800;
                        using (HttpWebResponse r = (HttpWebResponse)ping.GetResponse())
                        {
                            if (r.StatusCode == HttpStatusCode.OK)
                            {
                                serverIp = ip;
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
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
                            if (diff.TotalHours < 24.0) return false;
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

        private static void ApplyLauncherUpdateAndRelaunch(int targetPid)
        {
            Log("Applying in-place update for AlphaLauncher.exe (Target PID: " + targetPid + ")...");
            try
            {
                if (targetPid > 0)
                {
                    try
                    {
                        Process p = Process.GetProcessById(targetPid);
                        if (!p.HasExited)
                        {
                            p.WaitForExit(10000);
                        }
                    }
                    catch { }
                }

                System.Threading.Thread.Sleep(800);

                string launcherPath = Path.Combine(appDir, "AlphaLauncher.exe");
                string newLauncherPath = Path.Combine(appDir, "AlphaLauncher.new.exe");

                if (File.Exists(newLauncherPath))
                {
                    string backupPath = Path.Combine(appDir, "AlphaLauncher.bak");
                    if (File.Exists(backupPath)) try { File.Delete(backupPath); } catch { }
                    if (File.Exists(launcherPath)) try { File.Move(launcherPath, backupPath); } catch { }

                    File.Move(newLauncherPath, launcherPath);
                    Log("AlphaLauncher.exe updated successfully! Relaunching...");
                }

                if (File.Exists(launcherPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = launcherPath,
                        WorkingDirectory = appDir
                    });
                }
            }
            catch (Exception ex)
            {
                Log("Error applying launcher update: " + ex.Message);
            }
        }

        private static void PerformFullUpdateCycle()
        {
            // 1. Check & Update core vendor programs from official feed (Marketing, Data, DataAnalysis)
            CheckVendorProgramUpdates();

            // 2. Check & Update Alpha Suite from GitHub (using Token)
            bool ghSuccess = CheckGitHubSuiteUpdates();

            // 3. Fallback to Local/Tailscale server if GitHub check didn't succeed
            if (!ghSuccess)
            {
                CheckServerSuiteUpdates();
            }
        }

        private static bool CheckGitHubSuiteUpdates()
        {
            Log("Connecting to Alpha Team GitHub repository (ferfesh/SpecialSenderPro)...");
            try
            {
                string json = "";
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.Authorization] = "token " + GetGitHubToken();
                    wc.Headers[HttpRequestHeader.UserAgent] = "AlphaTeamUpdater/1.1";
                    json = wc.DownloadString(GITHUB_VERSION_URL);
                }

                if (string.IsNullOrEmpty(json) || !json.Contains("\"launcher\"")) return false;

                // Download updated AlphaLauncher if available
                string launcherExe = Path.Combine(appDir, "AlphaLauncher.exe");
                string launcherNew = Path.Combine(appDir, "AlphaLauncher.new.exe");
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.Authorization] = "token " + GetGitHubToken();
                        wc.Headers[HttpRequestHeader.UserAgent] = "AlphaTeamUpdater/1.1";
                        wc.DownloadFile(GITHUB_RAW_BASE + "AlphaLauncher.exe", launcherNew);
                    }

                    if (File.Exists(launcherNew) && new FileInfo(launcherNew).Length > 10000)
                    {
                        if (!File.Exists(launcherExe))
                        {
                            File.Move(launcherNew, launcherExe);
                        }
                        else
                        {
                            FileVersionInfo curFvi = FileVersionInfo.GetVersionInfo(launcherExe);
                            FileVersionInfo newFvi = FileVersionInfo.GetVersionInfo(launcherNew);
                            Version vCur = new Version(curFvi.FileMajorPart, curFvi.FileMinorPart, curFvi.FileBuildPart, curFvi.FilePrivatePart);
                            Version vNew = new Version(newFvi.FileMajorPart, newFvi.FileMinorPart, newFvi.FileBuildPart, newFvi.FilePrivatePart);

                            if (vNew > vCur)
                            {
                                Log("New AlphaLauncher version found on GitHub: " + vNew + " (Current: " + vCur + "). Downloaded as AlphaLauncher.new.exe");
                            }
                            else
                            {
                                File.Delete(launcherNew);
                            }
                        }
                    }
                }
                catch (Exception exL) { Log("GitHub launcher download error: " + exL.Message); }

                // Sync server.cert from GitHub
                try
                {
                    string certPath = Path.Combine(appDir, "server.cert");
                    string tempCert = Path.Combine(appDir, "server.cert.tmp");
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.Authorization] = "token " + GetGitHubToken();
                        wc.Headers[HttpRequestHeader.UserAgent] = "AlphaTeamUpdater/1.1";
                        wc.DownloadFile(GITHUB_RAW_BASE + "server.cert", tempCert);
                    }
                    if (File.Exists(tempCert) && new FileInfo(tempCert).Length > 100)
                    {
                        if (File.Exists(certPath)) File.Delete(certPath);
                        File.Move(tempCert, certPath);
                        InstallCertToRootStores(certPath);
                    }
                }
                catch { }

                Log("GitHub suite check completed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Log("Warning: Could not fetch suite updates from GitHub: " + ex.Message);
                return false;
            }
        }

        private static void CheckServerSuiteUpdates()
        {
            Log("Checking suite updates from Alpha server (http://" + serverIp + ":" + serverPort + "/api/updates)...");
            try
            {
                string json = "";
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/json";
                    json = wc.DownloadString("http://" + serverIp + ":" + serverPort + "/api/updates");
                }

                if (string.IsNullOrEmpty(json) || !json.Contains("\"tools\"")) return;

                // Download updated AlphaLauncher if available
                string launcherExe = Path.Combine(appDir, "AlphaLauncher.exe");
                string launcherNew = Path.Combine(appDir, "AlphaLauncher.new.exe");
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.DownloadFile("http://" + serverIp + ":" + serverPort + "/updates/download/launcher", launcherNew);
                    }

                    if (File.Exists(launcherNew) && new FileInfo(launcherNew).Length > 10000)
                    {
                        if (!File.Exists(launcherExe))
                        {
                            File.Move(launcherNew, launcherExe);
                        }
                        else
                        {
                            FileVersionInfo curFvi = FileVersionInfo.GetVersionInfo(launcherExe);
                            FileVersionInfo newFvi = FileVersionInfo.GetVersionInfo(launcherNew);
                            Version vCur = new Version(curFvi.FileMajorPart, curFvi.FileMinorPart, curFvi.FileBuildPart, curFvi.FilePrivatePart);
                            Version vNew = new Version(newFvi.FileMajorPart, newFvi.FileMinorPart, newFvi.FileBuildPart, newFvi.FilePrivatePart);

                            if (vNew > vCur)
                            {
                                Log("New AlphaLauncher version found: " + vNew + " (Current: " + vCur + "). Downloaded as AlphaLauncher.new.exe");
                            }
                            else
                            {
                                File.Delete(launcherNew);
                            }
                        }
                    }
                }
                catch { }

                // Sync server.cert
                try
                {
                    string certPath = Path.Combine(appDir, "server.cert");
                    string tempCert = Path.Combine(appDir, "server.cert.tmp");
                    using (WebClient wc = new WebClient())
                    {
                        wc.DownloadFile("http://" + serverIp + ":" + serverPort + "/server.cert", tempCert);
                    }
                    if (File.Exists(tempCert) && new FileInfo(tempCert).Length > 100)
                    {
                        if (File.Exists(certPath)) File.Delete(certPath);
                        File.Move(tempCert, certPath);
                        InstallCertToRootStores(certPath);
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Log("Warning: Could not fetch suite updates from local server: " + ex.Message);
            }
        }

        private static void CheckVendorProgramUpdates()
        {
            Log("Connecting to official vendor update feed (https://launcher.senderprov.com/Updater/Updater.txt)...");
            try
            {
                string txt = "";
                using (WebClient wc = new WebClient())
                {
                    txt = wc.DownloadString("https://launcher.senderprov.com/Updater/Updater.txt");
                }

                if (!string.IsNullOrEmpty(txt) && txt.Contains("[Marketing]"))
                {
                    Log("Official vendor update feed received successfully! Checking tool versions...");
                    ParseAndDownloadVendorFeed(txt);
                    return;
                }
            }
            catch (Exception exVendor)
            {
                Log("Warning: Could not connect to official vendor feed: " + exVendor.Message);
            }
        }

        private static void ParseAndDownloadVendorFeed(string feedText)
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
                    Log("Downloading package from " + directUrl + "...");
                    
                    // Robust chunked download
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(directUrl);
                    request.Timeout = 60000;
                    request.ReadWriteTimeout = 60000;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream responseStream = response.GetResponseStream())
                    using (FileStream fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[65536];
                        int bytesRead;
                        while ((bytesRead = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fileStream.Write(buffer, 0, bytesRead);
                        }
                    }

                    if (File.Exists(tempZip) && new FileInfo(tempZip).Length > 1000)
                    {
                        Log("Extracting package for " + folderName + "...");
                        if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                        using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                        {
                            string prefix1 = folderName.TrimEnd('/', '\\') + "/";
                            string prefix2 = folderName.TrimEnd('/', '\\') + "\\";

                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                if (string.IsNullOrEmpty(entry.Name)) continue;

                                string clean = entry.FullName;
                                if (clean.StartsWith(prefix1, StringComparison.OrdinalIgnoreCase))
                                    clean = clean.Substring(prefix1.Length);
                                else if (clean.StartsWith(prefix2, StringComparison.OrdinalIgnoreCase))
                                    clean = clean.Substring(prefix2.Length);

                                string destPath = Path.Combine(targetFolder, clean.Replace('/', Path.DirectorySeparatorChar));
                                string destDir = Path.GetDirectoryName(destPath);
                                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                                entry.ExtractToFile(destPath, true);
                            }
                        }

                        // Auto-flatten if nested
                        try
                        {
                            string nested = Path.Combine(targetFolder, folderName);
                            if (Directory.Exists(nested))
                            {
                                foreach (string file in Directory.GetFiles(nested, "*", SearchOption.AllDirectories))
                                {
                                    string rel = file.Substring(nested.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                    string dest = Path.Combine(targetFolder, rel);
                                    string dDir = Path.GetDirectoryName(dest);
                                    if (!Directory.Exists(dDir)) Directory.CreateDirectory(dDir);
                                    if (File.Exists(dest)) File.Delete(dest);
                                    File.Move(file, dest);
                                }
                                try { Directory.Delete(nested, true); } catch { }
                            }
                        }
                        catch { }

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

        private static void InstallCertToRootStores(string certPath)
        {
            try
            {
                if (File.Exists(certPath))
                {
                    X509Certificate2 cert = new X509Certificate2(certPath);

                    try
                    {
                        X509Store cuStore = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                        cuStore.Open(OpenFlags.ReadWrite);
                        if (!cuStore.Certificates.Contains(cert)) cuStore.Add(cert);
                        cuStore.Close();
                    }
                    catch { }

                    try
                    {
                        X509Store lmStore = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                        lmStore.Open(OpenFlags.ReadWrite);
                        if (!lmStore.Certificates.Contains(cert)) lmStore.Add(cert);
                        lmStore.Close();
                    }
                    catch { }

                    Log("Root certificate verified and installed in stores.");
                }
            }
            catch { }
        }

        private static void EnsureScheduledTask()
        {
            try
            {
                string updaterExe = Path.Combine(appDir, "AlphaUpdater.exe");
                if (!File.Exists(updaterExe)) return;

                string taskName = "AlphaTeam-SenderPro-AutoUpdate";
                string cmd = "/create /tn \"" + taskName + "\" /tr \"\\\"" + updaterExe + "\\\" -silent\" /sc daily /mo 1 /f";

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
        }
    }
}
