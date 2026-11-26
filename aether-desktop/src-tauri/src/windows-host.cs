using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AetherStandalone
{
    internal static class Program
    {
        private static readonly Dictionary<string, Assembly> Assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        [STAThread]
        private static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow());
        }
        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.WinForms") return null;
            lock (Assemblies)
            {
                Assembly cached;
                if (Assemblies.TryGetValue(name, out cached)) return cached;
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AetherStandalone.deps." + name + ".dll"))
                {
                    if (stream == null) throw new FileNotFoundException("WebView2 dependency missing: " + name);
                    byte[] bytes = new byte[stream.Length]; stream.Read(bytes, 0, bytes.Length);
                    cached = Assembly.Load(bytes); Assemblies[name] = cached; return cached;
                }
            }
        }
    }

    internal sealed class MainWindow : Form
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Panel layout = new Panel();
        private readonly Panel webToolbar = new Panel();
        private readonly Button backButton = new Button();
        private readonly WebView2 appView = new WebView2();
        private readonly WebView2 veniceView = new WebView2();
        private string assetsDirectory;
        private string ledgerPath;
        private string projectRoot;
        private bool buildRunning;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectoryW(string path);

        internal MainWindow()
        {
            Text = "Aether | Venice AI + App Builder + Bench";
            Size = new Size(1360, 900);
            MinimumSize = new Size(840, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(9, 11, 16);

            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "bench");
            Directory.CreateDirectory(data);
            ledgerPath = Path.Combine(data, "HASHES.csv");
            EnsureLedger();

            assetsDirectory = Path.Combine(Path.GetTempPath(), "Aether-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(assetsDirectory);
            Extract("AetherStandalone.assets.index.html", "index.html");
            Extract("AetherStandalone.assets.app.css", "app.css");
            Extract("AetherStandalone.assets.app.js", "app.js");
            Extract("AetherStandalone.deps.WebView2Loader.dll", "WebView2Loader.dll");
            if (!SetDllDirectoryW(assetsDirectory)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            layout.Dock = DockStyle.Fill;
            appView.Dock = DockStyle.Fill;
            veniceView.Dock = DockStyle.Fill;
            veniceView.Visible = false;
            webToolbar.Dock = DockStyle.Top;
            webToolbar.Height = 40;
            webToolbar.Visible = false;
            webToolbar.BackColor = Color.FromArgb(15, 19, 26);
            backButton.Text = "Back to Aether";
            backButton.AutoSize = true;
            backButton.Location = new Point(12, 6);
            backButton.Click += delegate { ShowLocalApp(); };
            webToolbar.Controls.Add(backButton);
            layout.Controls.Add(appView);
            layout.Controls.Add(veniceView);
            layout.Controls.Add(webToolbar);
            Controls.Add(layout);
            Load += InitializeWebViews;
            FormClosed += CleanupAssets;
        }

        private void Extract(string resourceName, string filename)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new FileNotFoundException("Bundled app asset missing: " + resourceName);
                using (FileStream output = File.Create(Path.Combine(assetsDirectory, filename))) input.CopyTo(output);
            }
        }

        private void EnsureLedger()
        {
            if (!File.Exists(ledgerPath)) File.WriteAllText(ledgerPath, "name,sha256,bytes,checked_at" + Environment.NewLine, new UTF8Encoding(true));
        }

        private async void InitializeWebViews(object sender, EventArgs args)
        {
            try
            {
                string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "WebView2");
                Directory.CreateDirectory(profile);
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, profile, null);
                await appView.EnsureCoreWebView2Async(environment);
                await veniceView.EnsureCoreWebView2Async(environment);

                appView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                appView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                appView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                appView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                appView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                appView.CoreWebView2.WebResourceRequested += FilterLocalRequests;
                appView.CoreWebView2.NavigationStarting += FilterLocalNavigation;
                appView.CoreWebView2.WebMessageReceived += HandleAppMessage;
                appView.CoreWebView2.SetVirtualHostNameToFolderMapping("aether.local", assetsDirectory, CoreWebView2HostResourceAccessKind.DenyCors);

                veniceView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                veniceView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                veniceView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
                veniceView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
                veniceView.CoreWebView2.NavigationStarting += delegate { ShowVenice(); };
                veniceView.CoreWebView2.NewWindowRequested += HandleVenicePopup;

                appView.CoreWebView2.Navigate("https://aether.local/index.html");
                Send(new { channel = "aether.bench.event", @event = "ready", ledgerPath = ledgerPath });
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Install Microsoft Edge WebView2 Runtime and restart Aether.\r\n\r\n" + error.Message,
                    "Aether startup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private void CleanupAssets(object sender, FormClosedEventArgs args) { try { if (Directory.Exists(assetsDirectory)) Directory.Delete(assetsDirectory, true); } catch { } }

        private object ReadLedger()
        {
            EnsureLedgerFile();
            List<object> records = new List<object>();
            foreach (string line in File.ReadAllLines(ledgerPath, Encoding.UTF8).Skip(1))
            {
                string[] fields = ParseCsvRow(line);
                if (fields.Length >= 4) records.Add(new { name = fields[0], sha256 = fields[1], bytes = fields[2], checkedAt = fields[3] });
            }
            return new { records = records, ledgerPath = ledgerPath };
        }

        private object HashFile()
        {
            using (OpenFileDialog picker = new OpenFileDialog { Title = "Choose file for SHA-256", CheckFileExists = true, Multiselect = false })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) throw new OperationCanceledException();
                FileInfo file = new FileInfo(picker.FileName);
                string digest;
                using (FileStream input = File.OpenRead(file.FullName))
                using (SHA256 hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                string checkedAt = DateTime.Now.ToString("o");
                File.AppendAllText(ledgerPath, QuoteCsv(file.Name) + "," + digest + "," + file.Length + "," + QuoteCsv(checkedAt) + Environment.NewLine, new UTF8Encoding(true));
                return new { record = new { name = file.Name, sha256 = digest, bytes = file.Length, checkedAt = checkedAt }, ledgerPath = ledgerPath };
            }
        }

        private void OpenVenice()
        {
            veniceView.CoreWebView2.Navigate("https://venice.ai/chat/agent");
            ShowVenice();
        }

        private void ShowVenice()
        {
            appView.Visible = false;
            veniceView.Visible = true;
            webToolbar.Visible = true;
            veniceView.BringToFront();
            webToolbar.BringToFront();
        }

        private void ShowLocalApp()
        {
            veniceView.Visible = false;
            webToolbar.Visible = false;
            appView.Visible = true;
            appView.BringToFront();
        }

        private void HandleVenicePopup(object sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            Uri uri;
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out uri) && uri.Scheme == "https")
            {
                args.Handled = true;
                veniceView.CoreWebView2.Navigate(uri.AbsoluteUri);
                ShowVenice();
            }
        }

        private void FilterLocalNavigation(object sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (!args.Uri.StartsWith("https://aether.local/", StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
        }

        private void FilterLocalRequests(object sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            try { if (new Uri(args.Request.Uri).Host.Equals("aether.local", StringComparison.OrdinalIgnoreCase)) return; } catch { }
            args.Response = appView.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(new byte[0]), 403, "Blocked", "Content-Type: text/plain");
        }

        private void HandleAppMessage(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            string id = "";
            string action = "";
            try
            {
                Dictionary<string, object> request = json.Deserialize<Dictionary<string, object>>(args.WebMessageAsJson);
                if (request == null || !request.ContainsKey("channel") || Convert.ToString(request["channel"]) != "aether.bench" || !request.ContainsKey("action")) return;
                action = Convert.ToString(request["action"]);
                if (request.ContainsKey("id")) id = Convert.ToString(request["id"]);
                if (action == "init") { Send(new { channel = "aether.bench.event", @event = "ready", ledgerPath = ledgerPath }); return; }
                if (String.IsNullOrEmpty(id)) return;
                if (action == "venice.open") { OpenVenice(); Reply(id, action, new { opened = true }); return; }
                if (action == "project.build") { StartProjectBuild(id); return; }
                object result;
                if (action == "devices.scan") result = ScanDevices();
                else if (action == "adb.connect") result = ConnectAdb(GetString(request, "address"));
                else if (action == "ledger.list") result = ReadLedger();
                else if (action == "ledger.hash") result = HashFile();
                else if (action == "project.select") result = SelectProject();
                else if (action == "project.files") result = ListProjectFiles();
                else if (action == "project.read") result = ReadProjectFile(GetString(request, "relativePath"));
                else if (action == "project.save") result = SaveProjectFile(GetString(request, "relativePath"), GetString(request, "contents"));
                else if (action == "project.veniceContext") result = CopyProjectContext(GetString(request, "relativePath"), GetString(request, "task"));
                else throw new InvalidOperationException("Unsupported Aether operation.");
                Reply(id, action, result);
            }
            catch (Exception error)
            {
                if (!String.IsNullOrEmpty(id)) Send(new { channel = "aether.bench.result", id = id, ok = false, action = action, error = DescribeError(error) });
            }
        }

        private static string GetString(IDictionary<string, object> data, string key) { return data.ContainsKey(key) ? Convert.ToString(data[key]) ?? "" : ""; }
        private void Reply(string id, string action, object result) { Send(new { channel = "aether.bench.result", id = id, ok = true, action = action, result = result }); }
        private void Send(object value) { if (appView.CoreWebView2 != null) appView.CoreWebView2.PostWebMessageAsJson(json.Serialize(value)); }
        private static string DescribeError(Exception error)
        {
            if (error is OperationCanceledException) return "Operation canceled.";
            if (error is UnauthorizedAccessException) return "Windows denied access to this project or file.";
            if (error is ManagementException) return "Windows USB inventory failed.";
            return error.Message;
        }

        private object ScanDevices()
        {
            List<Dictionary<string, object>> devices = new List<Dictionary<string, object>>();
            using (ManagementObjectSearcher search = new ManagementObjectSearcher("SELECT PNPDeviceID,Name,Manufacturer,Status FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'"))
            using (ManagementObjectCollection rows = search.Get())
            {
                foreach (ManagementBaseObject row in rows)
                {
                    string id = Convert.ToString(row["PNPDeviceID"]);
                    string name = Convert.ToString(row["Name"]);
                    string details = ((id ?? "") + " " + (name ?? "") + " " + Convert.ToString(row["Manufacturer"])).ToLowerInvariant();
                    string type = ClassifyUsb(details);
                    if (type == null) continue;
                    devices.Add(new Dictionary<string, object> { { "name", String.IsNullOrWhiteSpace(name) ? type : name }, { "serial", id ?? "" }, { "transport", "Windows USB inventory" }, { "state", Convert.ToString(row["Status"]) }, { "type", type }, { "vid", UsbId(id, "VID_") }, { "pid", UsbId(id, "PID_") } });
                }
            }
            bool adb = false, fastboot = false;
            string[] drives = Environment.GetLogicalDrives();
            foreach (string tool in FindAndroidTool("adb.exe", drives))
            {
                adb = true;
                foreach (string line in RunTool(tool, "devices -l").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) || line.StartsWith("* ", StringComparison.Ordinal)) continue;
                    string[] fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 2 || fields[1] == "offline" || HasSerial(devices, fields[0])) continue;
                    string model = fields.FirstOrDefault(field => field.StartsWith("model:", StringComparison.OrdinalIgnoreCase));
                    devices.Add(new Dictionary<string, object> { { "name", model == null ? "Android device" : model.Substring(6).Replace('_', ' ') }, { "serial", fields[0] }, { "transport", "ADB device list" }, { "state", fields[1] }, { "type", "Android / ADB" } });
                }
                break;
            }
            foreach (string tool in FindAndroidTool("fastboot.exe", drives))
            {
                fastboot = true;
                foreach (string line in RunTool(tool, "devices").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length == 0 || HasSerial(devices, fields[0])) continue;
                    devices.Add(new Dictionary<string, object> { { "name", "Android bootloader" }, { "serial", fields[0] }, { "transport", "Fastboot device list" }, { "state", fields.Length > 1 ? fields[1] : "connected" }, { "type", "Fastboot" } });
                }
                break;
            }
            return new { devices = devices, adbAvailable = adb, fastbootAvailable = fastboot };
        }

        private object ConnectAdb(string address)
        {
            if (String.IsNullOrWhiteSpace(address) || !Regex.IsMatch(address, @"^[A-Za-z0-9.-]+:\d{1,5}$")) throw new ArgumentException("Enter a wireless ADB address in host:port format.");
            int port = Int32.Parse(address.Substring(address.LastIndexOf(':') + 1));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("address", "ADB port must be between 1 and 65535.");
            string adb = FindAndroidTool("adb.exe", Environment.GetLogicalDrives()).FirstOrDefault();
            if (adb == null) throw new FileNotFoundException("Install official Android SDK Platform-Tools to use ADB attach.");
            return new { message = RunTool(adb, "connect " + address), devices = RunTool(adb, "devices -l") };
        }

        private static bool HasSerial(List<Dictionary<string, object>> devices, string serial) { return devices.Any(device => String.Equals(Convert.ToString(device["serial"]), serial, StringComparison.OrdinalIgnoreCase)); }
        private static string ClassifyUsb(string text)
        {
            if (text.Contains("android") || text.Contains("adb")) return "Android / USB";
            if (text.Contains("samsung")) return "Samsung USB";
            if (text.Contains("qualcomm") || text.Contains("sahara") || text.Contains("edl")) return "Qualcomm USB";
            if (text.Contains("mediatek") || text.Contains("mtk") || text.Contains("preloader") || text.Contains("brom")) return "MediaTek USB";
            if (text.Contains("fastboot") || text.Contains("bootloader")) return "Android bootloader";
            return null;
        }
        private static string UsbId(string id, string marker) { if (String.IsNullOrEmpty(id)) return ""; int index = id.IndexOf(marker, StringComparison.OrdinalIgnoreCase); return index < 0 || id.Length < index + marker.Length + 4 ? "" : id.Substring(index + marker.Length, 4).ToLowerInvariant(); }
        private static IEnumerable<string> FindAndroidTool(string exe, string[] drives)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string sdk = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? Environment.GetEnvironmentVariable("ANDROID_HOME");
            if (!String.IsNullOrWhiteSpace(sdk)) AddFile(paths, Path.Combine(sdk, "platform-tools", exe));
            AddFile(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", exe));
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) if (!String.IsNullOrWhiteSpace(directory)) AddFile(paths, Path.Combine(directory.Trim().Trim('"'), exe));
            foreach (string drive in drives) foreach (string relative in new[] { "platform-tools\\" + exe, "Android\\Sdk\\platform-tools\\" + exe }) AddFile(paths, Path.Combine(drive, relative));
            return paths;
        }
        private static void AddFile(HashSet<string> paths, string path) { try { if (File.Exists(path)) paths.Add(path); } catch { } }
        private static string RunTool(string executable, string arguments)
        {
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo { FileName = executable, Arguments = arguments, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                process.Start(); StringBuilder output = new StringBuilder(); process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) output.AppendLine(e.Data); }; process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) output.AppendLine(e.Data); };
                process.BeginOutputReadLine(); process.BeginErrorReadLine(); if (!process.WaitForExit(15000)) { try { process.Kill(); } catch { } throw new TimeoutException("ADB command timed out."); }
                process.WaitForExit(); return output.ToString().Trim();
            }
        }

        private object SelectProject()
        {
            using (FolderBrowserDialog picker = new FolderBrowserDialog { Description = "Choose the project folder to attach" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) throw new OperationCanceledException();
                projectRoot = Path.GetFullPath(picker.SelectedPath);
                if (!Directory.Exists(projectRoot)) throw new DirectoryNotFoundException("Selected project folder is unavailable.");
                return new { projectName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), projectPath = projectRoot };
            }
        }
        private static bool SkipProjectDirectory(string name) { return new[] { ".git", ".gradle", ".idea", ".aether-backup", "node_modules", "vendor", "build", "dist", "target", "bin", "obj", "packages", "venv", "__pycache__" }.Contains(name, StringComparer.OrdinalIgnoreCase); }
        private static bool IsEditableProjectFile(string name)
        {
            string[] extensions = { ".cs", ".java", ".kt", ".kts", ".gradle", ".xml", ".json", ".yml", ".yaml", ".properties", ".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".py", ".go", ".rs", ".md", ".txt", ".sh", ".ps1", ".toml", ".swift", ".m", ".mm", ".h", ".cpp", ".c" };
            return name.Equals("gradlew", StringComparison.OrdinalIgnoreCase) || name.Equals("gradlew.bat", StringComparison.OrdinalIgnoreCase) || extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
        }
        private object ListProjectFiles()
        {
            RequireProject(); List<object> files = new List<object>();
            foreach (string file in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(SkipProjectDirectory)) continue;
                FileInfo info = new FileInfo(file); if (info.Length > 512 * 1024 || !IsEditableProjectFile(info.Name)) continue;
                files.Add(new { relativePath = relative.Replace(Path.DirectorySeparatorChar, '/'), bytes = info.Length }); if (files.Count >= 1000) break;
            }
            return new { projectName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), projectPath = projectRoot, files = files };
        }
        private void RequireProject() { if (String.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot)) throw new InvalidOperationException("Attach a project folder first."); }
        private string ResolveProjectFile(string relative)
        {
            RequireProject(); relative = (relative ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative)) throw new InvalidOperationException("Project paths must be relative.");
            string full = Path.GetFullPath(Path.Combine(projectRoot, relative)); string prefix = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("File path is outside the attached project.");
            FileInfo info = new FileInfo(full); if (!info.Exists || info.Length > 512 * 1024 || !IsEditableProjectFile(info.Name)) throw new InvalidOperationException("File is missing, unsupported, or larger than 512 KiB.");
            return full;
        }
        private object ReadProjectFile(string relative) { return new { relativePath = relative, contents = File.ReadAllText(ResolveProjectFile(relative), Encoding.UTF8) }; }
        private object SaveProjectFile(string relative, string contents)
        {
            if (contents == null || Encoding.UTF8.GetByteCount(contents) > 512 * 1024) throw new InvalidOperationException("Edited file exceeds the 512 KiB limit.");
            string full = ResolveProjectFile(relative); string backupRoot = Path.Combine(projectRoot, ".aether-backup"); Directory.CreateDirectory(backupRoot);
            string safe = relative.Replace('/', Path.DirectorySeparatorChar); string backup = Path.Combine(backupRoot, safe + "." + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".bak");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(full, backup, false); File.WriteAllText(full, contents, new UTF8Encoding(false));
            return new { relativePath = relative, backupPath = backup };
        }
        private object CopyProjectContext(string relative, string task)
        {
            if (String.IsNullOrWhiteSpace(task) || task.Length > 4000) throw new InvalidOperationException("Task must be 1 to 4,000 characters.");
            string source = File.ReadAllText(ResolveProjectFile(relative), Encoding.UTF8);
            string context = "Help build or change this app. Return a revised full file and test steps. Do not claim to edit local files; I will review and apply changes in Aether App Builder.\r\n\r\nTask:\r\n" + task + "\r\n\r\nFile: " + relative + "\r\n\r\n```\r\n" + source + "\r\n```";
            Clipboard.SetText(context); return new { copied = true, relativePath = relative };
        }
        private void StartProjectBuild(string id)
        {
            RequireProject(); string wrapper = Path.Combine(projectRoot, "gradlew.bat"); if (!File.Exists(wrapper)) wrapper = Path.Combine(projectRoot, "gradlew");
            if (!File.Exists(wrapper)) throw new FileNotFoundException("Gradle wrapper not found in attached project root.");
            ProcessStartInfo info = new ProcessStartInfo { FileName = wrapper.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ? (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") : wrapper, Arguments = wrapper.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ? "/d /s /c \"\"" + wrapper + "\" assembleDebug\"" : "assembleDebug", WorkingDirectory = projectRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            Process process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) SendBuildLine(id, e.Data); };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) SendBuildLine(id, e.Data); };
            process.Exited += delegate
            {
                try
                {
                    process.WaitForExit(); string apk = null; string output = Path.Combine(projectRoot, "app", "build", "outputs", "apk", "debug");
                    if (Directory.Exists(output)) apk = Directory.GetFiles(output, "*.apk").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    Send(new { channel = "aether.bench.result", id = id, ok = true, action = "project.build", result = new { exitCode = process.ExitCode, apkPath = process.ExitCode == 0 ? apk : null } });
                }
                finally { process.Dispose(); }
            };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        }
        private void SendBuildLine(string id, string line) { Send(new { channel = "aether.builder.event", id = id, @event = "build.output", line = line }); }

        private void EnsureLedgerFile() { if (!File.Exists(ledgerPath)) File.WriteAllText(ledgerPath, "name,sha256,bytes,checked_at" + Environment.NewLine, new UTF8Encoding(true)); }
        private static string QuoteCsv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        private static string[] ParseCsvRow(string line)
        {
            List<string> fields = new List<string>(); StringBuilder current = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < line.Length; i++) { char c = line[i]; if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else quoted = !quoted; } else if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Length = 0; } else current.Append(c); }
            fields.Add(current.ToString()); return fields.ToArray();
        }
        private void Cleanup(object sender, FormClosedEventArgs args) { try { if (Directory.Exists(assetsDirectory)) Directory.Delete(assetsDirectory, true); } catch { } }
    }
}
