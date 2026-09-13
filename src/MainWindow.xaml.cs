using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace PRomda.FileShare;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<string> localFiles = new();
    private readonly ObservableCollection<string> remoteFiles = new();
    private Server? server;
    private ClientSession? clientSession;
    private TaskCompletionSource<bool>? accessDecision;
    private readonly SemaphoreSlim accessApprovalGate = new(1, 1);
    private string? libraryId;
    private string? folder;
    private string? currentHost;
    private int currentPort;
    private string? currentCode;
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Forms.ContextMenuStrip trayMenu;
    private bool allowRealClose;

    public MainWindow()
    {
        InitializeComponent();
        LocalFilesList.ItemsSource = localFiles;
        FilesList.ItemsSource = remoteFiles;
        AllowDrop = true;
        folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FPI");
        Directory.CreateDirectory(folder);
        FolderText.Text = folder;
        RefreshLocalFiles();

        trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("Открыть PRomda FileShare", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("Закрыть PRomda FileShare", null, (_, _) => ExitApplication());

        trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "PRomda FileShare",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private string LocalHost()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                    return ua.Address.ToString();
            }
        }
        return "127.0.0.1";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureLibraryFolder()) return;

        if (server == null)
        {
            libraryId = Guid.NewGuid().ToString("N");
            currentHost = LocalHost();
            currentPort = 28741;
            currentCode = CodeGenerator.CreateLibraryCode(currentHost, currentPort, libraryId);
            server = new Server(currentPort, ApproveAccess);
            server.AddLibrary(libraryId, folder!);
            server.Start();
        }

        MyCode.Text = $"Код: {currentCode}";
        CodeBox.Text = currentCode;
        Status.Text = "ONLINE";
        RemoteStatus.Text = $"Сервер запущен • {currentHost}:{currentPort}";
        RefreshLocalFiles();
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        EnsureLibraryFolder();
    }

    private void ClearAllFiles_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        var files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).ToArray() : Array.Empty<string>();
        if (files.Length == 0)
        {
            RemoteStatus.Text = "Папка FPI уже пуста.";
            return;
        }

        var result = WpfMessageBox.Show(
            $"Удалить все файлы из папки FPI?\n\nБудет удалено файлов: {files.Length}\nЭто действие нельзя отменить.",
            "Очистить все файлы", WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Warning);
        if (result != WpfMessageBoxResult.Yes) return;

        var deleted = 0;
        foreach (var file in files)
        {
            try { File.Delete(file); deleted++; } catch { }
        }
        RefreshLocalFiles();
        RemoteStatus.Text = $"Удалено файлов: {deleted}";
    }

    private void PickFiles_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureLibraryFolder()) return;

        using var dlg = new Forms.OpenFileDialog
        {
            Title = "Выберите файлы для библиотеки",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
        CopyFilesToLibrary(dlg.FileNames);
    }

    private void DropZone_DragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(WpfDataFormats.FileDrop) ? WpfDragDropEffects.Copy : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop)) return;
        if (!EnsureLibraryFolder()) return;

        var paths = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
        CopyFilesToLibrary(paths.Where(File.Exists));
        e.Handled = true;
    }

    private void CopyFilesToLibrary(IEnumerable<string> paths)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);

        int copied = 0;
        foreach (var source in paths)
        {
            try
            {
                var target = GetUniqueTargetPath(folder, Path.GetFileName(source));
                File.Copy(source, target, overwrite: false);
                copied++;
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Не удалось добавить файл:\n{Path.GetFileName(source)}\n\n{ex.Message}",
                    "PRomda FileShare", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            }
        }

        RefreshLocalFiles();
        if (copied > 0) RemoteStatus.Text = $"Добавлено файлов: {copied}";
    }

    private static string GetUniqueTargetPath(string directory, string fileName)
    {
        var safeName = string.IsNullOrWhiteSpace(fileName) ? "file" : fileName;
        var path = Path.Combine(directory, safeName);
        if (!File.Exists(path)) return path;

        var name = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        for (int i = 2; i < 100000; i++)
        {
            path = Path.Combine(directory, $"{name} ({i}){ext}");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("Не удалось подобрать свободное имя файла.");
    }

    private bool EnsureLibraryFolder()
    {
        if (string.IsNullOrWhiteSpace(folder))
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FPI");
        try
        {
            Directory.CreateDirectory(folder);
            FolderText.Text = folder;
            return true;
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Не удалось создать папку FPI в Документах.\n\n{ex.Message}",
                "PRomda FileShare", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            return false;
        }
    }

    private void RefreshLocalFiles()
    {
        localFiles.Clear();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(file);
                localFiles.Add($"{info.Name}  •  {FormatSize(info.Length)}");
            }
            catch { }
        }
    }

    private async Task<bool> ApproveAccess(AccessRequest r)
    {
        await accessApprovalGate.WaitAsync();
        try
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Dispatcher.InvokeAsync(() =>
            {
                accessDecision = tcs;
                AccessToastUser.Text = string.IsNullOrWhiteSpace(r.RemoteName) ? "Другой пользователь" : r.RemoteName;
                AccessToastCode.Text = $"Код: {r.LibraryCode}";
                AccessToast.Visibility = Visibility.Visible;
            });

            return await tcs.Task;
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(accessDecision, null)) return;
                AccessToast.Visibility = Visibility.Collapsed;
                accessDecision = null;
            });
            accessApprovalGate.Release();
        }
    }

    private void AccessAllow_Click(object sender, RoutedEventArgs e)
    {
        accessDecision?.TrySetResult(true);
    }

    private void AccessDeny_Click(object sender, RoutedEventArgs e)
    {
        accessDecision?.TrySetResult(false);
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (!CodeGenerator.TryParse(code, out var host, out var port, out var id))
        {
            WpfMessageBox.Show("Неверный код библиотеки.", "PRomda FileShare", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return;
        }

        currentCode = id;
        currentHost = host;
        currentPort = port;

        try
        {
            clientSession?.Dispose();
            clientSession = null;
            var c = new Client();
            var r = await c.ConnectAsync(host, port, id, Environment.UserName);
            remoteFiles.Clear();

            if (r.session == null)
            {
                RemoteStatus.Text = r.message;
                return;
            }

            clientSession = r.session;
            foreach (var f in r.files) remoteFiles.Add(f);
            RemoteStatus.Text = "Подключено. Доступ уже разрешён — можно скачивать файлы без повторных запросов.";
        }
        catch (Exception ex)
        {
            clientSession?.Dispose();
            clientSession = null;
            RemoteStatus.Text = "Не удалось подключиться: " + ex.Message;
        }
    }

    private async void DownloadAll_Click(object sender, RoutedEventArgs e)
    {
        if (clientSession == null || remoteFiles.Count == 0)
        {
            WpfMessageBox.Show("Сначала подключитесь к библиотеке с файлами.", "PRomda FileShare", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            return;
        }

        using var dlg = new Forms.FolderBrowserDialog
        {
            Description = "Выберите папку, куда сохранить все файлы",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath)) return;

        var total = remoteFiles.Count;
        var completed = 0;
        var failed = 0;

        foreach (var file in remoteFiles.ToArray())
        {
            var cleanFileName = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(cleanFileName))
            {
                failed++;
                continue;
            }

            var destination = GetUniqueTargetPath(dlg.SelectedPath, cleanFileName);
            try
            {
                var progress = new Progress<long>(bytes =>
                {
                    RemoteStatus.Text = $"Скачивание {completed + 1}/{total}: {cleanFileName} · {FormatSize(bytes)}";
                });
                var ok = await clientSession.DownloadAsync(cleanFileName, destination, progress);
                if (ok) completed++; else failed++;
            }
            catch
            {
                failed++;
            }
        }

        RemoteStatus.Text = failed == 0
            ? $"Готово. Скачано файлов: {completed}."
            : $"Скачивание завершено. Успешно: {completed}, ошибок: {failed}.";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        trayIcon.ShowBalloonTip(1200, "PRomda FileShare", "Приложение продолжает работать в скрытых значках.", Forms.ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void ExitApplication()
    {
        allowRealClose = true;
        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayMenu.Dispose();
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private static string FormatSize(long n)
    {
        string[] u = { "Б", "КБ", "МБ", "ГБ" };
        double d = n;
        int i = 0;
        while (d >= 1024 && i < u.Length - 1) { d /= 1024; i++; }
        return $"{d:0.##} {u[i]}";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!allowRealClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        accessDecision?.TrySetResult(false);
        clientSession?.Dispose();
        server?.Stop();
        if (trayIcon.Visible) trayIcon.Visible = false;
        trayIcon.Dispose();
        trayMenu.Dispose();
        accessApprovalGate.Dispose();
        base.OnClosed(e);
    }
}
