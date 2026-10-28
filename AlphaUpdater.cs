using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace AlphaTeamUpdater
{
    class Program
    {
        private const string GITHUB_RAW_BASE = "https://raw.githubusercontent.com/ferfesh/SpecialSenderPro/main/";
        private static string appDir;
        private static string logPath;

        static void Main(string[] args)
        {
            try
            {
                appDir = AppDomain.CurrentDomain.BaseDirectory;
                logPath = Path.Combine(appDir, "updater.log");

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

                Log("=== Starting Dual-Source Update Check ===");

                // 1. Check Source A: Official Vendor Server for Core Tools
                CheckVendorOfficialUpdates();

                // 2. Check Source B: GitHub for Our Developed Launcher & Configs
                CheckGitHubToolingUpdates();

                Log("=== Update Check Completed Successfully ===");
            }
            catch (Exception ex)
            {
                Log("Update Error: " + ex.Message);
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

        // ==========================================
        // Source 1: Official Vendor Program Updates
        // ==========================================
        private static void CheckVendorOfficialUpdates()
        {
            try
            {
                Log("Checking Official Vendor Server for program updates...");
                
                // Read vendor updater info (from local server gateway or vendor host)
                // Tools to check: Marketing, Data, DataAnalysis
                CheckAndDownloadToolPackage("Sender Pro Marketing", "Sender Pro.exe", "/updates/download/marketing");
                CheckAndDownloadToolPackage("Sender Pro Data", "Sender Pro.exe", "/updates/download/data");
                CheckAndDownloadToolPackage("Sender Pro DataAnalysis", "Sender Pro Data Analysis.exe", "/updates/download/data-analysis");
            }
            catch (Exception ex)
            {
                Log("Vendor Check Warning: " + ex.Message);
            }
        }

        private static void CheckAndDownloadToolPackage(string folderName, string exeName, string relativeDownloadUrl)
        {
            try
            {
                string toolFolder = Path.Combine(appDir, folderName);
                string toolExe = Path.Combine(toolFolder, exeName);

                // If folder doesn't exist, we don't force auto-update silently unless instructed
                if (!Directory.Exists(toolFolder) && !File.Exists(toolExe))
                {
                    return;
                }

                // Check version against server manifest if accessible
                string serverIp = GetServerIp();
                string manifestUrl = "http://" + serverIp + ":8080/api/updates";

                using (WebClient wc = new WebClient())
                {
                    string json = wc.DownloadString(manifestUrl);
                    if (json.Contains("\"exists\":true"))
                    {
                        Log("Server has active package for " + folderName);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Tool check skipped for " + folderName + ": " + ex.Message);
            }
        }

        // ==========================================
        // Source 2: Our Developed Version on GitHub
        // ==========================================
        private static void CheckGitHubToolingUpdates()
        {
            try
            {
                Log("Checking GitHub (ferfesh/SpecialSenderPro) for our custom tooling updates...");

                string versionJsonUrl = GITHUB_RAW_BASE + "version.json";
                string jsonContent = "";

                using (WebClient wc = new WebClient())
                {
                    jsonContent = wc.DownloadString(versionJsonUrl);
                }

                if (!string.IsNullOrEmpty(jsonContent))
                {
                    Log("GitHub version manifest retrieved successfully.");

                    // 1. Check AlphaLauncher.exe
                    string launcherPath = Path.Combine(appDir, "AlphaLauncher.exe");
                    string launcherDownloadUrl = GITHUB_RAW_BASE + "AlphaLauncher.exe";

                    // Update launcher if missing or updated
                    if (!File.Exists(launcherPath))
                    {
                        Log("Downloading missing AlphaLauncher.exe from GitHub...");
                        using (WebClient wc = new WebClient())
                        {
                            wc.DownloadFile(launcherDownloadUrl, launcherPath);
                        }
                    }

                    // 2. Ensure server.cert is up to date and installed
                    string certPath = Path.Combine(appDir, "server.cert");
                    string certDownloadUrl = GITHUB_RAW_BASE + "server.cert";
                    using (WebClient wc = new WebClient())
                    {
                        wc.DownloadFile(certDownloadUrl, certPath);
                    }
                    InstallCertToCurrentUser(certPath);

                    // 3. Ensure server.conf is present
                    string confPath = Path.Combine(appDir, "server.conf");
                    if (!File.Exists(confPath))
                    {
                        using (WebClient wc = new WebClient())
                        {
                            wc.DownloadFile(GITHUB_RAW_BASE + "server.conf", confPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("GitHub Check Warning: " + ex.Message);
            }
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
                        Log("Installed root certificate from GitHub into CurrentUser store.");
                    }
                    store.Close();
                }
            }
            catch (Exception ex)
            {
                Log("Certificate install warning: " + ex.Message);
            }
        }

        private static string GetServerIp()
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
            return "127.0.0.1";
        }
    }
}
