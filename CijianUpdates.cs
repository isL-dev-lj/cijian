using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

internal static class CijianUpdates
{
    private const string Channel = "https://github.com/isL-dev-lj/cijian/releases/latest";
    private const string PublisherKey = "<RSAKeyValue><Modulus>zGhlm5JqT9ElXL1+8qDT2AjV5T+rPVukW+n1eKdN1nx0iZL0Tahvpp26Y2Ofw5K8ZNOIWzcarX+T7fTieaZV2Hgc1UahnfTZr6zoHCh7oGJL73IQ1TrULSNyfqdB21dHuCYWzwZzI9OQ4Ab3IjWred9b6lS9XFzv9LbslhmWtfJywd05DgCjgDJT9gOcKpTGsQG/rgTy6UPMaTkCERBNCehJGs5fkHiw1LhmIldIffCn8/uHvL2a++N3NSy11PGraMuns67FH9UYz/+Pv1S7Y6uMAomlAoGIhjEtJskQzTnt1oBp7T5I83M2m675rx4Wmy1LdQCN0WeOco+qpBAoSUA9cijKrFxwJ+ohORmZUYfHLOAnv1ToZg9kalCH6A6CLZFm5FeAQ3923MjDL4UvkdU5zrwg8MmnqzEJGyvIxJh7yY/29sbOzfI0JMBluChnbmXEQCKcYnX6yRwMYA+U9WuILLtTtZNsqtjlXjsRYgegJ34lzDYAsWjTBjh+dmeV</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    private static Version CurrentVersion { get { return typeof(Program).Assembly.GetName().Version; } }
    internal static string DisplayVersion { get { return CurrentVersion.ToString(3); } }

    internal static bool ShowDialog()
    {
        using (var dialog = new Form { Text = "词间 · 软件更新", Icon = Program.AppIcon,
            Font = new Font("Microsoft YaHei UI", 10), ClientSize = new Size(450, 150),
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false })
        {
            dialog.Controls.Add(new Label { Text = "当前版本 " + DisplayVersion + "\n请从词间更新页下载更新包，然后在这里安装。",
                Bounds = new Rectangle(20, 20, 410, 60) });
            var download = new Button { Text = "打开更新页", Bounds = new Rectangle(20, 92, 155, 34) };
            var install = new Button { Text = "安装更新包…", Bounds = new Rectangle(188, 92, 155, 34) };
            download.Click += (sender, e) =>
            {
                try { Process.Start(new ProcessStartInfo(Channel) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception error) { ShowError(error.Message); }
            };
            install.Click += (sender, e) =>
            {
                using (var picker = new OpenFileDialog { Filter = "词间更新包 (*.zip)|*.zip", CheckFileExists = true })
                {
                    if (picker.ShowDialog(dialog) != DialogResult.OK) return;
                    try
                    {
                        string candidate = ExtractPackage(picker.FileName);
                        try
                        {
                            Version next = ReadVersion(candidate);
                            if (next <= CurrentVersion)
                            {
                                MessageBox.Show(dialog, "当前已是 " + DisplayVersion + "，请选择更高版本的更新包。", "词间");
                                return;
                            }
                            if (MessageBox.Show(dialog, "安装词间 " + next.ToString(3) + "？安装后会自动重开。",
                                    "词间", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                            string destination = Application.ExecutablePath;
                            int pid = Process.GetCurrentProcess().Id;
                            var helper = Process.Start(new ProcessStartInfo(candidate,
                                "--install-update " + pid + " " + Quote(destination) + " " + CurrentVersion)
                                { UseShellExecute = false, CreateNoWindow = true });
                            if (helper == null) throw new IOException("无法启动更新程序。");
                            helper.Dispose();
                            candidate = null;
                            dialog.DialogResult = DialogResult.OK;
                        }
                        finally { if (candidate != null) File.Delete(candidate); }
                    }
                    catch (Exception error) { ShowError(error.Message); }
                }
            };
            dialog.Controls.AddRange(new Control[] { download, install });
            return dialog.ShowDialog() == DialogResult.OK;
        }
    }

    private static string ExtractPackage(string zipPath)
    {
        byte[] signature = ReadPackageSignature(zipPath);
        string path = Path.Combine(Path.GetTempPath(), "Cijian-update-" + Guid.NewGuid().ToString("N") + ".exe");
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var entry = archive.GetEntry("Cijian.exe");
            if (archive.Entries.Count != 1 || entry == null || entry.Length < 1 || entry.Length > 32 * 1024 * 1024)
                throw new InvalidDataException("更新包应该只包含一个 Cijian.exe。");
            try
            {
                entry.ExtractToFile(path);
                using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(PublisherKey);
                    if (!rsa.VerifyData(File.ReadAllBytes(path), "SHA256", signature))
                        throw new InvalidDataException("更新包签名无效，请重新从词间发布页下载。");
                }
            }
            catch { if (File.Exists(path)) File.Delete(path); throw; }
        }
        return path;
    }

    private static byte[] ReadPackageSignature(string zipPath)
    {
        const int signatureLength = 384;
        const int endRecordLength = 22;
        using (var stream = File.OpenRead(zipPath))
        using (var reader = new BinaryReader(stream))
        {
            if (stream.Length >= signatureLength + endRecordLength)
            {
                stream.Seek(-(signatureLength + endRecordLength), SeekOrigin.End);
                if (reader.ReadUInt32() == 0x06054b50)
                {
                    stream.Seek(16, SeekOrigin.Current);
                    if (reader.ReadUInt16() == signatureLength) return reader.ReadBytes(signatureLength);
                }
            }
        }
        throw new InvalidDataException("此更新包缺少词间发布签名。");
    }

    private static Version ReadVersion(string path)
    {
        var name = AssemblyName.GetAssemblyName(path);
        if (!name.Name.Equals("Cijian", StringComparison.OrdinalIgnoreCase) ||
            FileVersionInfo.GetVersionInfo(path).ProductName != Program.AppName)
            throw new InvalidDataException("无法识别这是词间的更新包。");
        return name.Version;
    }

    private static string Quote(string path) { return "\"" + path + "\""; }

    internal static int Apply(string parentId, string destination, string oldVersion)
    {
        try
        {
            int pid = int.Parse(parentId);
            Process parent;
            try { parent = Process.GetProcessById(pid); }
            catch (ArgumentException) { parent = null; }
            using (parent)
                if (parent != null && !parent.WaitForExit(20000))
                    throw new IOException("旧版尚未退出，请重开词间后再试。");
            ReplaceAndRestart(Application.ExecutablePath, destination, Version.Parse(oldVersion), true);
            return 0;
        }
        catch (Exception error) { ShowError(error.Message); return 1; }
    }

    private static void ReplaceAndRestart(string candidate, string target, Version expected, bool restart)
    {
        // Legacy executables may have been compiled under a versioned assembly name.
        if (AssemblyName.GetAssemblyName(target).Version != expected ||
            FileVersionInfo.GetVersionInfo(target).ProductName != Program.AppName || ReadVersion(candidate) <= expected)
            throw new InvalidDataException("现有程序或更新包的版本已变化，请重新选择更新包。");
        string pending = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string backup = pending + ".bak";
        try
        {
            File.Copy(candidate, pending);
            File.Replace(pending, target, backup, true);
            try
            {
                if (restart)
                {
                    using (var process = Process.Start(new ProcessStartInfo(target,
                        "--cleanup-update " + Quote(candidate))
                        { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) }))
                        if (process == null) throw new IOException("无法重开词间。");
                }
            }
            catch { File.Replace(backup, target, null, true); throw; }
            try { File.Delete(backup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    internal static void Cleanup(string path)
    {
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) ||
            !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), @"^Cijian-update-[0-9a-f]{32}\.exe$")) return;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try { File.Delete(full); return; }
            catch (IOException) { System.Threading.Thread.Sleep(60); }
            catch (UnauthorizedAccessException) { return; }
        }
    }

    internal static int TestPackage(string directory)
    {
        string candidate = ExtractPackage(Path.Combine(directory, "Cijian-Update.zip"));
        try
        {
            string target = Path.Combine(directory, "old.exe");
            Version old = AssemblyName.GetAssemblyName(target).Version;
            ReplaceAndRestart(candidate, target, old, false);
            if (ReadVersion(target) != CurrentVersion) return 61;
            try { ReplaceAndRestart(candidate, target, CurrentVersion, false); return 62; }
            catch (InvalidDataException) { }
            var tampered = File.ReadAllBytes(Path.Combine(directory, "Cijian-Update.zip"));
            tampered[tampered.Length - 1] ^= 1;
            string tamperedPath = Path.Combine(directory, "tampered.zip");
            File.WriteAllBytes(tamperedPath, tampered);
            try
            {
                try { File.Delete(ExtractPackage(tamperedPath)); return 64; }
                catch (InvalidDataException) { return 0; }
            }
            finally { File.Delete(tamperedPath); }
        }
        finally { File.Delete(candidate); }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "词间 · 更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
