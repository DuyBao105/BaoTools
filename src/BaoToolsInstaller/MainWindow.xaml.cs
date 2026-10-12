using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BaoToolsInstaller;

public record InstallerLanguage(string Tag, string Display, string Flag)
{
    public string DisplayWithFlag => $"{Flag}  {Display}";
}

public partial class MainWindow : Window
{
    private string _installDir;
    private string _selectedLangTag = "en";
    private bool _isUpgrade = false;

    private readonly List<InstallerLanguage> _languages = new()
    {
        new("en", "English", "🇺🇸"),
        new("vi", "Tiếng Việt", "🇻🇳"),
        new("zh-Hans", "简体中文", "🇨🇳"),
        new("zh-Hant", "繁體中文", "🇹🇼"),
        new("ja", "日本語", "🇯🇵"),
        new("ko", "한국어", "🇰🇷"),
        new("es", "Español", "🇪🇸"),
        new("es-419", "Español (LatAm)", "🇲🇽"),
        new("pt-BR", "Português (BR)", "🇧🇷"),
        new("pt-PT", "Português (PT)", "🇵🇹"),
        new("fr", "Français", "🇫🇷"),
        new("de", "Deutsch", "🇩🇪"),
        new("ru", "Русский", "🇷🇺"),
        new("uk", "Українська", "🇺🇦"),
        new("it", "Italiano", "🇮🇹"),
        new("nl", "Nederlands", "🇳🇱"),
        new("pl", "Polski", "🇵🇱"),
        new("tr", "Türkçe", "🇹🇷"),
        new("ar", "العربية", "🇸🇦"),
        new("th", "ไทย", "🇹🇭"),
        new("id", "Bahasa Indonesia", "🇮🇩"),
        new("cs", "Čeština", "🇨🇿"),
        new("hu", "Magyar", "🇭🇺"),
        new("ro", "Română", "🇷🇴"),
        new("el", "Ελληνικά", "🇬🇷"),
        new("bg", "Български", "🇧🇬"),
        new("da", "Dansk", "🇩🇰"),
        new("fi", "Suomi", "🇫🇮"),
        new("nb", "Norsk", "🇳🇴"),
        new("sv", "Svenska", "🇸🇪"),
    };

    public MainWindow()
    {
        InitializeComponent();

        // Detect if an existing version of BaoTools is already installed
        string? existingPath = DetectExistingInstallation();
        if (!string.IsNullOrEmpty(existingPath) && Directory.Exists(existingPath))
        {
            _isUpgrade = true;
            _installDir = existingPath;
        }
        else
        {
            _installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "BaoTools");
        }

        TxtInstallPath.Text = _installDir;
        UpdateDiskSpaceInfo(_installDir);

        // Initialize Languages
        CmbLanguage.ItemsSource = _languages;
        CmbLanguage.DisplayMemberPath = "Display";
        CmbLanguage.SelectedValuePath = "Tag";
        CmbLanguage.SelectedValue = "en";

        LoadAppIcon();

        Loaded += MainWindow_Loaded;
    }

    private static bool HasWritePermission(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            string testFile = Path.Combine(path, ".write_test_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(testFile, "ok");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? DetectExistingInstallation()
    {
        // 1. Prefer %LOCALAPPDATA%\Programs\BaoTools (User-level standard)
        string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BaoTools");
        if (File.Exists(Path.Combine(localAppData, "BaoTools.exe")) && HasWritePermission(localAppData))
        {
            return localAppData;
        }

        // 2. Check HKCU uninstall registry
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\BaoTools");
            if (key?.GetValue("InstallLocation") is string loc && Directory.Exists(loc) && File.Exists(Path.Combine(loc, "BaoTools.exe")) && HasWritePermission(loc))
            {
                return loc.TrimEnd('\\', '/');
            }
        }
        catch { }

        // 3. Check HKLM uninstall registry ONLY if writable without admin elevation
        string[] rootSubKeys = [
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        ];
        foreach (var sub in rootSubKeys)
        {
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(sub);
                if (root != null)
                {
                    foreach (var name in root.GetSubKeyNames())
                    {
                        if (name.Contains("BaoTools", StringComparison.OrdinalIgnoreCase))
                        {
                            using var appKey = root.OpenSubKey(name);
                            if (appKey?.GetValue("InstallLocation") is string loc && Directory.Exists(loc) && File.Exists(Path.Combine(loc, "BaoTools.exe")) && HasWritePermission(loc))
                            {
                                return loc.TrimEnd('\\', '/');
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // 4. Check known Program Files path ONLY if writable
        string programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BaoTools");
        if (File.Exists(Path.Combine(programFiles, "BaoTools.exe")) && HasWritePermission(programFiles))
        {
            return programFiles;
        }

        return null;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--screenshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                string targetPath = args[i + 1];
                await Task.Delay(400);
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        int w = (int)Math.Max(ActualWidth, 680);
                        int h = (int)Math.Max(ActualHeight, 450);
                        var rtb = new RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        rtb.Render(this);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(rtb));
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
                        using var fs = File.Create(targetPath);
                        encoder.Save(fs);
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(targetPath + ".err", ex.ToString());
                    }
                    Application.Current.Shutdown(0);
                });
                break;
            }
        }
    }

    private void LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
            var bi = new BitmapImage(uri);
            TitleIcon.Source = bi;
            HeroLogo.Source = bi;
            return;
        }
        catch { }

        try
        {
            string localIcon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", "BaoToolsGui", "icon.ico");
            if (!File.Exists(localIcon))
            {
                localIcon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            }

            if (File.Exists(localIcon))
            {
                var bi = new BitmapImage(new Uri(localIcon, UriKind.Absolute));
                TitleIcon.Source = bi;
                HeroLogo.Source = bi;
            }
        }
        catch { }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbLanguage.SelectedItem is InstallerLanguage lang)
        {
            _selectedLangTag = lang.Tag;
            ApplyLanguage(lang.Tag);
        }
    }

    private void TxtInstallPath_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDiskSpaceInfo(TxtInstallPath.Text);
    }

    private void UpdateDiskSpaceInfo(string? path)
    {
        if (TxtDiskSpace == null) return;

        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "C:\\" : path)) ?? "C:\\";
            var drive = new DriveInfo(root);
            double freeGb = (double)drive.AvailableFreeSpace / (1024 * 1024 * 1024);

            string reqLabel = _selectedLangTag == "vi" ? "Cần" : "Required";
            string availLabel = _selectedLangTag == "vi" ? "Trống" : "Available";

            TxtDiskSpace.Text = $"{reqLabel}: ~20 MB  •  {availLabel}: {freeGb:F1} GB";
        }
        catch
        {
            TxtDiskSpace.Text = "Required: ~20 MB";
        }
    }

    private void ApplyLanguage(string tag)
    {
        if (tag == "vi")
        {
            Title = "BaoTools Setup";
            TxtHeroTitle.Text = _isUpgrade ? "Cập nhật BaoTools" : "Cài đặt BaoTools";
            TxtHeroSubtitle.Text = _isUpgrade
                ? "Phát hiện phiên bản cũ • Nâng cấp lên v10.5.0"
                : "Phiên bản 10.5.0 • Trình quản lý Game đa năng";
            LblInstallLocation.Text = "THƯ MỤC CÀI ĐẶT";
            BtnBrowse.Content = "Duyệt...";
            ChkDesktopIcon.Content = "Tạo lối tắt ngoài màn hình chính (Desktop)";
            BtnInstall.Content = _isUpgrade ? "Cập nhật" : "Cài đặt";
            TxtTerms.Text = "Nhấn tiếp tục đồng nghĩa với việc bạn đồng ý với các điều khoản.";
            TxtInstallingTitle.Text = _isUpgrade ? "Đang cập nhật BaoTools..." : "Đang cài đặt BaoTools...";
            TxtStatus.Text = "Đang giải nén tài nguyên...";
            TxtCompleteTitle.Text = _isUpgrade ? "Cập nhật hoàn tất" : "Cài đặt hoàn tất";
            TxtCompleteSubtitle.Text = _isUpgrade
                ? "BaoTools đã được nâng cấp thành công lên v10.5.0."
                : "BaoTools đã được cài đặt thành công và sẵn sàng khởi chạy.";
            BtnLaunch.Content = "Mở BaoTools";
        }
        else if (tag == "zh-Hans")
        {
            Title = "BaoTools 安装程序";
            TxtHeroTitle.Text = _isUpgrade ? "更新 BaoTools" : "安装 BaoTools";
            TxtHeroSubtitle.Text = _isUpgrade ? "检测到现有版本 • 升级至 v10.5.0" : "版本 10.5.0 • 一体化游戏管理器";
            LblInstallLocation.Text = "安装路径";
            BtnBrowse.Content = "浏览...";
            ChkDesktopIcon.Content = "创建桌面快捷方式";
            BtnInstall.Content = _isUpgrade ? "更新" : "安装";
            TxtTerms.Text = "点击即表示您同意软件许可条款。";
            TxtInstallingTitle.Text = "正在更新 BaoTools...";
            TxtStatus.Text = "正在解压文件...";
            TxtCompleteTitle.Text = "更新完成";
            TxtCompleteSubtitle.Text = "BaoTools 已成功升级至 v10.5.0。";
            BtnLaunch.Content = "启动 BaoTools";
        }
        else if (tag == "zh-Hant")
        {
            Title = "BaoTools 安裝程式";
            TxtHeroTitle.Text = _isUpgrade ? "更新 BaoTools" : "安裝 BaoTools";
            TxtHeroSubtitle.Text = _isUpgrade ? "檢測到現有版本 • 升級至 v10.5.0" : "版本 10.5.0 • 一體化遊戲管理器";
            LblInstallLocation.Text = "安裝路徑";
            BtnBrowse.Content = "瀏覽...";
            ChkDesktopIcon.Content = "建立桌面捷徑";
            BtnInstall.Content = _isUpgrade ? "更新" : "安裝";
            TxtTerms.Text = "點擊即表示您同意軟體許可條款。";
            TxtInstallingTitle.Text = "正在更新 BaoTools...";
            TxtStatus.Text = "正在解壓縮檔案...";
            TxtCompleteTitle.Text = "更新完成";
            TxtCompleteSubtitle.Text = "BaoTools 已成功升級至 v10.5.0。";
            BtnLaunch.Content = "啟動 BaoTools";
        }
        else if (tag == "ja")
        {
            Title = "BaoTools セットアップ";
            TxtHeroTitle.Text = _isUpgrade ? "BaoTools のアップデート" : "BaoTools のインストール";
            TxtHeroSubtitle.Text = _isUpgrade ? "既存のバージョンを検出 • v10.5.0 へアップグレード" : "バージョン 10.5.0 • オールインワンゲームマネージャー";
            LblInstallLocation.Text = "インストール先";
            BtnBrowse.Content = "参照...";
            ChkDesktopIcon.Content = "デスクトップショートカットを作成";
            BtnInstall.Content = _isUpgrade ? "アップデート" : "インストール";
            TxtTerms.Text = "クリックすると利用規約に同意したことになります。";
            TxtInstallingTitle.Text = "BaoTools を更新中...";
            TxtStatus.Text = "ファイルを展開中...";
            TxtCompleteTitle.Text = "完了";
            TxtCompleteSubtitle.Text = "BaoTools の更新が完了しました。";
            BtnLaunch.Content = "BaoTools を起動";
        }
        else if (tag == "ko")
        {
            Title = "BaoTools 설치";
            TxtHeroTitle.Text = _isUpgrade ? "BaoTools 업데이트" : "BaoTools 설치";
            TxtHeroSubtitle.Text = _isUpgrade ? "기존 버전 감지 • v10.5.0 업그레이드" : "버전 10.5.0 • 올인원 게임 매니저";
            LblInstallLocation.Text = "설치 위치";
            BtnBrowse.Content = "찾아보기...";
            ChkDesktopIcon.Content = "바탕화면 바로가기 만들기";
            BtnInstall.Content = _isUpgrade ? "업데이트" : "설치";
            TxtTerms.Text = "클릭하면 소프트웨어 약관에 동의하는 것으로 간주됩니다.";
            TxtInstallingTitle.Text = "BaoTools 업데이트 중...";
            TxtStatus.Text = "파일 압축 푸는 중...";
            TxtCompleteTitle.Text = "완료";
            TxtCompleteSubtitle.Text = "BaoTools 가 v10.5.0 으로 성공적으로 업데이트되었습니다.";
            BtnLaunch.Content = "BaoTools 실행";
        }
        else if (tag == "ru")
        {
            Title = "Установка BaoTools";
            TxtHeroTitle.Text = _isUpgrade ? "Обновление BaoTools" : "Установка BaoTools";
            TxtHeroSubtitle.Text = _isUpgrade ? "Обнаружена предыдущая версия • Обновление до v10.5.0" : "Версия 10.5.0 • Универсальный менеджер игр";
            LblInstallLocation.Text = "ПУТЬ УСТАНОВКИ";
            BtnBrowse.Content = "Обзор...";
            ChkDesktopIcon.Content = "Создать ярлык на рабочем столе";
            BtnInstall.Content = _isUpgrade ? "Обновить" : "Установить";
            TxtTerms.Text = "Нажимая кнопку, вы принимаете условия соглашения.";
            TxtInstallingTitle.Text = "Обновление BaoTools...";
            TxtStatus.Text = "Распаковка файлов...";
            TxtCompleteTitle.Text = "Завершено";
            TxtCompleteSubtitle.Text = "BaoTools успешно обновлен до v10.5.0.";
            BtnLaunch.Content = "Запустить BaoTools";
        }
        else
        {
            // Default English
            Title = "BaoTools Setup";
            TxtHeroTitle.Text = _isUpgrade ? "Update BaoTools" : "Install BaoTools";
            TxtHeroSubtitle.Text = _isUpgrade
                ? "Existing version detected • Upgrade to v10.5.0"
                : "Version 10.5.0 • All-in-one Game Manager";
            LblInstallLocation.Text = "INSTALL LOCATION";
            BtnBrowse.Content = "Browse...";
            ChkDesktopIcon.Content = "Create desktop shortcut";
            BtnInstall.Content = _isUpgrade ? "Update" : "Install";
            TxtTerms.Text = "By clicking to proceed, you agree to the software terms.";
            TxtInstallingTitle.Text = _isUpgrade ? "Updating BaoTools..." : "Installing BaoTools...";
            TxtStatus.Text = "Extracting files...";
            TxtCompleteTitle.Text = _isUpgrade ? "Update Complete" : "Installation Complete";
            TxtCompleteSubtitle.Text = _isUpgrade
                ? "BaoTools has been successfully updated to v10.5.0."
                : "BaoTools has been installed and is ready to launch.";
            BtnLaunch.Content = "Launch BaoTools";
        }

        UpdateDiskSpaceInfo(TxtInstallPath.Text);
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select BaoTools installation folder",
            InitialDirectory = TxtInstallPath.Text
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            string chosen = dlg.FolderName.Trim();
            if (Path.GetFileName(chosen).Equals("BaoTools", StringComparison.OrdinalIgnoreCase))
            {
                TxtInstallPath.Text = chosen;
            }
            else
            {
                TxtInstallPath.Text = Path.Combine(chosen, "BaoTools");
            }
        }
    }

    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        _installDir = TxtInstallPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(_installDir))
        {
            MessageBox.Show("Please choose a valid installation directory!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ViewReady.Visibility = Visibility.Collapsed;
        ViewInstalling.Visibility = Visibility.Visible;

        bool createDesktop = ChkDesktopIcon.IsChecked == true;

        bool success = await Task.Run(() => PerformInstallation(_installDir, createDesktop));

        if (success)
        {
            ViewInstalling.Visibility = Visibility.Collapsed;
            ViewComplete.Visibility = Visibility.Visible;
        }
        else
        {
            MessageBox.Show("An error occurred during installation. Please check write permissions and try again!",
                "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ViewInstalling.Visibility = Visibility.Collapsed;
            ViewReady.Visibility = Visibility.Visible;
        }
    }

    private bool PerformInstallation(string targetDir, bool createDesktop)
    {
        try
        {
            // Verify write permission. If target is read-only (e.g. Program Files without admin), fallback to LocalAppData
            if (!HasWritePermission(targetDir))
            {
                targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BaoTools");
            }
            _installDir = targetDir;

            // Close any currently running BaoTools processes so files are not locked
            foreach (var proc in Process.GetProcessesByName("BaoTools"))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(1500);
                }
                catch { }
            }

            Directory.CreateDirectory(targetDir);

            // Save user-selected language into %AppData%\BaoToolsGui\settings.json
            SaveSelectedLanguageToSettings(_selectedLangTag);

            // Get payload.zip embedded in this assembly
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("payload.zip");

            if (stream == null)
            {
                // Fallback: look for local zip in same directory or release folder
                string localZip = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BaoTools_v10.5_Portable.zip");
                if (!File.Exists(localZip))
                {
                    localZip = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "release_v10.5", "BaoTools_v10.5_Portable.zip");
                }
                if (!File.Exists(localZip)) return false;

                using var fs = File.OpenRead(localZip);
                ExtractWithProgress(fs, targetDir);
            }
            else
            {
                ExtractWithProgress(stream, targetDir);
            }

            // Create Shortcuts
            string mainExe = Path.Combine(targetDir, "BaoTools.exe");
            if (File.Exists(mainExe))
            {
                // Start Menu Shortcut
                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BaoTools");
                Directory.CreateDirectory(startMenuDir);
                CreateShortcut(Path.Combine(startMenuDir, "BaoTools.lnk"), mainExe);

                // Desktop Shortcut
                if (createDesktop)
                {
                    try
                    {
                        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        if (Directory.Exists(desktopDir))
                        {
                            CreateShortcut(Path.Combine(desktopDir, "BaoTools.lnk"), mainExe);
                        }

                        string desktopDir2 = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        if (!string.IsNullOrEmpty(desktopDir2) && Directory.Exists(desktopDir2) &&
                            !string.Equals(desktopDir, desktopDir2, StringComparison.OrdinalIgnoreCase))
                        {
                            CreateShortcut(Path.Combine(desktopDir2, "BaoTools.lnk"), mainExe);
                        }

                        // Clean up legacy public desktop shortcut to prevent duplicate icons
                        try
                        {
                            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                            string legacyLnk = Path.Combine(commonDesktop, "BaoTools.lnk");
                            if (File.Exists(legacyLnk))
                            {
                                File.Delete(legacyLnk);
                            }
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error creating desktop shortcut: {ex.Message}");
                    }
                }

                // Register Windows Add / Remove Programs
                RegisterUninstaller(targetDir, mainExe);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return false;
        }
    }

    private static void SaveSelectedLanguageToSettings(string langTag)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string settingsDir = Path.Combine(appData, "BaoToolsGui");
            Directory.CreateDirectory(settingsDir);
            string settingsPath = Path.Combine(settingsDir, "settings.json");

            var jsonObject = new JsonObject();
            if (File.Exists(settingsPath))
            {
                try
                {
                    string existingJson = File.ReadAllText(settingsPath);
                    var node = JsonNode.Parse(existingJson);
                    if (node is JsonObject jo)
                    {
                        jsonObject = jo;
                    }
                }
                catch { }
            }

            // Persist the exact chosen language
            jsonObject["Language"] = langTag;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(settingsPath, jsonObject.ToJsonString(options));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write settings.json: {ex.Message}");
        }
    }

    private void ExtractWithProgress(Stream zipStream, string targetDir)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        int total = archive.Entries.Count;
        int count = 0;

        foreach (var entry in archive.Entries)
        {
            count++;
            string fullPath = Path.Combine(targetDir, entry.FullName);

            if (string.IsNullOrEmpty(entry.Name))
            {
                // Directory
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                entry.ExtractToFile(fullPath, overwrite: true);
            }

            int percent = (int)((double)count / total * 100);
            string fileName = entry.Name;

            Dispatcher.Invoke(() =>
            {
                InstallProgressBar.Value = percent;
                TxtPercent.Text = $"{percent}%";
                TxtStatus.Text = string.IsNullOrEmpty(fileName) ? "Creating directories..." : $"Extracting: {fileName}";
            });
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetExePath)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                object? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    object? shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    if (shortcut != null)
                    {
                        Type scType = shortcut.GetType();
                        scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExePath });
                        scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(targetExePath) ?? "" });
                        scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { $"{targetExePath},0" });
                        scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "BaoTools" });
                        scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, Array.Empty<object>());
                        return;
                    }
                }
            }
        }
        catch { }

        // Robust Fallback: PowerShell shortcut generation
        try
        {
            string escapedLnk = shortcutPath.Replace("'", "''");
            string escapedExe = targetExePath.Replace("'", "''");
            string escapedDir = (Path.GetDirectoryName(targetExePath) ?? "").Replace("'", "''");
            string psScript = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{escapedLnk}'); $s.TargetPath = '{escapedExe}'; $s.WorkingDirectory = '{escapedDir}'; $s.IconLocation = '{escapedExe},0'; $s.Description = 'BaoTools'; $s.Save();";
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{psScript}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }

    private static void RegisterUninstaller(string installDir, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\BaoTools");
            if (key != null)
            {
                key.SetValue("DisplayName", "BaoTools");
                key.SetValue("DisplayVersion", "10.5.0");
                key.SetValue("DisplayIcon", exePath);
                key.SetValue("InstallLocation", installDir);
                key.SetValue("Publisher", "Châu Tinh Trì");
                key.SetValue("UninstallString", $"cmd.exe /c rd /s /q \"{installDir}\"");
                key.SetValue("EstimatedSize", 20000); // ~20 MB
            }
        }
        catch { }
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string mainExe = Path.Combine(_installDir, "BaoTools.exe");
            if (File.Exists(mainExe))
            {
                Process.Start(new ProcessStartInfo(mainExe) { UseShellExecute = true });
            }
        }
        catch { }
        finally
        {
            Close();
        }
    }
}
