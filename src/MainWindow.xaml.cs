using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private DownloadProgressWindow? downloadProgressWindow;
    private NotificationWindow? notificationWindow;
    private TextBlock? bottomStatus;
    private TextBlock? bottomFiles;
    private System.Windows.Shapes.Ellipse? bottomDot;
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
        BuildStatusBar();

        trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("Открыть PRomda FileShare", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("Закрыть PRomda FileShare", null, (_, _) => ExitApplication());
        trayIcon = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "PRomda FileShare", Visible = true, ContextMenuStrip = trayMenu };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void BuildStatusBar()
    {
        if (Content is not Border root || root.Child is not Grid grid) return;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(8, 8, 8)), BorderBrush = new SolidColorBrush(Color.FromRgb(25, 25, 25)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 0) };
        var layout = new Grid();
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bottomDot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brushes.Gray, Margin = new Thickness(0, 0, 6, 0) };
        bottomStatus = new TextBlock { Text = "Не подключено", Foreground = new SolidColorBrush(Color.FromRgb(146, 146, 146)), FontSize = 10 };
        bottomFiles = new TextBlock { Text = "0 файлов", Foreground = new SolidColorBrush(Color.FromRgb(119, 119, 119)), FontSize = 10, Margin = new Thickness(8, 0, 0, 0) };
        left.Children.Add(bottomDot); left.Children.Add(bottomStatus); left.Children.Add(new TextBlock { Text = "  ·  ", Foreground = new SolidColorBrush(Color.FromRgb(64, 64, 64)), FontSize = 10 }); left.Children.Add(bottomFiles);
        layout.Children.Add(left); layout.Children.Add(new TextBlock { Text = "PRomda FileShare", HorizontalAlignment = HorizontalAlignment.Right, Foreground = new SolidColorBrush(Color.FromRgb(79, 79, 79)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        border.Child = layout; Grid.SetRow(border, 2); grid.Children.Add(border);
    }

    private void SetBottomStatus(string text, bool online = false) { bottomStatus!.Text = text; bottomDot!.Fill = online ? Brushes.White : Brushes.Gray; bottomFiles!.Text = $"{remoteFiles.Count} файлов"; }

    private string LocalHost()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address)) return ua.Address.ToString();
        }
        return "127.0.0.1";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureLibraryFolder()) return;
        if (server == null)
        {
            libraryId = Guid.NewGuid().ToString("N"); currentHost = LocalHost(); currentPort = 28741;
            currentCode = CodeGenerator.CreateLibraryCode(currentHost, currentPort, libraryId);
            server = new Server(currentPort, ApproveAccess); server.AddLibrary(libraryId, folder!); server.Start();
            ShowNotification("Библиотека создана", "Код создан для текущего запуска программы.");
        }
        MyCode.Text = $"Код: {currentCode}"; CodeBox.Text = currentCode; Status.Text = "ONLINE"; RemoteStatus.Text = $"Сервер запущен • {currentHost}:{currentPort}"; SetBottomStatus("Сервер запущен", true); RefreshLocalFiles();
    }

    private void Folder_Click(object sender, RoutedEventArgs e) => EnsureLibraryFolder();

    private void ClearAllFiles_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        var files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).ToArray() : Array.Empty<string>();
        if (files.Length == 0) { RemoteStatus.Text = "Папка FPI уже пуста."; return; }
        if (WpfMessageBox.Show($"Удалить все файлы из папки FPI?\n\nБудет удалено файлов: {files.Length}\nЭто действие нельзя отменить.", "Очистить все файлы", WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Warning) != WpfMessageBoxResult.Yes) return;
        var deleted = 0; foreach (var file in files) try { File.Delete(file); deleted++; } catch { }
        RefreshLocalFiles(); RemoteStatus.Text = $"Удалено файлов: {deleted}"; ShowNotification("Библиотека обновлена", $"Удалено файлов: {deleted}.");
    }

    private void PickFiles_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureLibraryFolder()) return;
        using var dlg = new Forms.OpenFileDialog { Title = "Выберите файлы для библиотеки", Multiselect = true, CheckFileExists = true };
        if (dlg.ShowDialog() == Forms.DialogResult.OK) CopyFilesToLibrary(dlg.FileNames);
    }

    private void DropZone_DragOver(object sender, WpfDragEventArgs e) { e.Effects = e.Data.GetDataPresent(WpfDataFormats.FileDrop) ? WpfDragDropEffects.Copy : WpfDragDropEffects.None; e.Handled = true; }
    private void DropZone_Drop(object sender, WpfDragEventArgs e) { if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop) || !EnsureLibraryFolder()) return; CopyFilesToLibrary((string[])e.Data.GetData(WpfDataFormats.FileDrop)); e.Handled = true; }

    private void CopyFilesToLibrary(IEnumerable<string> paths)
    {
        if (string.IsNullOrWhiteSpace(folder)) return; Directory.CreateDirectory(folder); var copied = 0;
        foreach (var source in paths.Where(File.Exists)) try { File.Copy(source, GetUniqueTargetPath(folder, Path.GetFileName(source))); copied++; } catch (Exception ex) { WpfMessageBox.Show($"Не удалось добавить файл:\n{Path.GetFileName(source)}\n\n{ex.Message}", "PRomda FileShare", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning); }
        RefreshLocalFiles(); if (copied > 0) { RemoteStatus.Text = $"Добавлено файлов: {copied}"; ShowNotification("Файлы добавлены", $"Добавлено файлов: {copied}."); }
    }

    private static string GetUniqueTargetPath(string directory, string fileName)
    {
        var safeName = string.IsNullOrWhiteSpace(fileName) ? "file" : fileName; var path = Path.Combine(directory, safeName); if (!File.Exists(path)) return path;
        var name = Path.GetFileNameWithoutExtension(safeName); var ext = Path.GetExtension(safeName);
        for (var i = 2; i < 100000; i++) { path = Path.Combine(directory, $"{name} ({i}){ext}"); if (!File.Exists(path)) return path; }
        throw new IOException("Не удалось подобрать свободное имя файла.");
    }

    private bool EnsureLibraryFolder()
    {
        if (string.IsNullOrWhiteSpace(folder)) folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FPI");
        try { Directory.CreateDirectory(folder); FolderText.Text = folder; return true; } catch (Exception ex) { WpfMessageBox.Show($"Не удалось создать папку FPI в Документах.\n\n{ex.Message}", "PRomda FileShare", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error); return false; }
    }

    private void RefreshLocalFiles()
    {
        localFiles.Clear(); if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)) try { var info = new FileInfo(file); localFiles.Add($"{info.Name}  •  {FormatSize(info.Length)}"); } catch { }
    }

    private async Task<bool> ApproveAccess(AccessRequest r)
    {
        await accessApprovalGate.WaitAsync();
        try { var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); await Dispatcher.InvokeAsync(() => { accessDecision = tcs; AccessToastUser.Text = string.IsNullOrWhiteSpace(r.RemoteName) ? "Другой пользователь" : r.RemoteName; AccessToastCode.Text = $"Код: {r.LibraryCode}"; AccessToast.Visibility = Visibility.Visible; }); return await tcs.Task; }
        finally { await Dispatcher.InvokeAsync(() => { AccessToast.Visibility = Visibility.Collapsed; accessDecision = null; }); accessApprovalGate.Release(); }
    }

    private void AccessAllow_Click(object sender, RoutedEventArgs e) => accessDecision?.TrySetResult(true);
    private void AccessDeny_Click(object sender, RoutedEventArgs e) => accessDecision?.TrySetResult(false);

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim(); if (!CodeGenerator.TryParse(code, out var host, out var port, out var id)) { ShowNotification("Ошибка", "Неверный код библиотеки."); return; }
        currentCode = code; currentHost = host; currentPort = port;
        try
        {
            clientSession?.Dispose(); clientSession = null; var c = new Client(); var r = await c.ConnectAsync(host, port, id, Environment.UserName);
            if (r.session == null) { RemoteStatus.Text = r.message; ShowNotification("Подключение отклонено", r.message); return; }
            clientSession = r.session; clientSession.FilesUpdated += OnRemoteFilesUpdated; remoteFiles.Clear(); foreach (var f in r.files) remoteFiles.Add(f);
            RemoteStatus.Text = "Подключено. Доступ разрешён — повторные запросы не нужны."; SetBottomStatus("Подключено", true); ShowNotification("Подключено", $"Доступно файлов: {remoteFiles.Count}.");
        }
        catch (Exception ex) { clientSession?.Dispose(); clientSession = null; RemoteStatus.Text = "Не удалось подключиться: " + ex.Message; SetBottomStatus("Ошибка подключения"); ShowNotification("Ошибка подключения", ex.Message); }
    }

    private void OnRemoteFilesUpdated(string[]? files)
    {
        Dispatcher.Invoke(() =>
        {
            var next = files ?? Array.Empty<string>(); var old = remoteFiles.ToHashSet(StringComparer.OrdinalIgnoreCase); var added = next.Where(x => !old.Contains(x)).ToArray(); var removed = old.Where(x => !next.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
            remoteFiles.Clear(); foreach (var file in next.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) remoteFiles.Add(file); bottomFiles!.Text = $"{remoteFiles.Count} файлов";
            if (added.Length > 0) ShowNotification("Библиотека обновлена", $"Добавлено: {string.Join(", ", added.Take(3))}{(added.Length > 3 ? "…" : "")}"); else if (removed.Length > 0) ShowNotification("Библиотека обновлена", $"Удалено файлов: {removed.Length}."); else ShowNotification("Библиотека обновлена", "Список файлов изменён.");
        });
    }

    private async void DownloadAll_Click(object sender, RoutedEventArgs e)
    {
        if (clientSession == null || remoteFiles.Count == 0) { ShowNotification("Скачивание", "Сначала подключитесь к библиотеке с файлами."); return; }
        using var dlg = new Forms.FolderBrowserDialog { Description = "Выберите папку, куда сохранить все файлы", UseDescriptionForTitle = true, ShowNewFolderButton = true }; if (dlg.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath)) return;
        var archivePath = Path.Combine(dlg.SelectedPath, "PRomda-Downloads.zip"); var total = remoteFiles.Count; var completed = 0; var failed = 0; downloadProgressWindow = new DownloadProgressWindow(); downloadProgressWindow.Show();
        try
        {
            foreach (var file in remoteFiles.ToArray())
            {
                var name = Path.GetFileName(file); if (string.IsNullOrWhiteSpace(name)) { failed++; continue; } var destination = GetUniqueTargetPath(dlg.SelectedPath, name);
                try
                {
                    var index = completed + failed + 1; var progress = new Progress<(long completed, long total)>(p => { downloadProgressWindow?.SetProgress(name, index, total, p.completed, p.total); RemoteStatus.Text = $"Скачивание {index}/{total}: {name} · {FormatSize(p.completed)} / {FormatSize(p.total)}"; });
                    if (await clientSession.DownloadAsync(name, destination, progress)) { await ArchiveHelper.AddFileAsync(destination, archivePath); completed++; } else failed++;
                }
                catch { failed++; }
            }
            downloadProgressWindow.SetCompleted(completed, total, failed); RemoteStatus.Text = failed == 0 ? $"Готово. Скачано файлов: {completed}. ZIP: {Path.GetFileName(archivePath)}" : $"Готово. Успешно: {completed}, ошибок: {failed}."; ShowNotification("Скачивание завершено", $"Файлов: {completed}. Архив: {Path.GetFileName(archivePath)}"); await Task.Delay(1000);
        }
        finally { downloadProgressWindow?.Close(); downloadProgressWindow = null; }
    }

    private void ShowNotification(string title, string message)
    {
        Dispatcher.Invoke(() => { notificationWindow?.Close(); notificationWindow = new NotificationWindow(title, message); notificationWindow.Owner = this; notificationWindow.Show(); _ = Task.Run(async () => { await Task.Delay(3200); try { await Dispatcher.InvokeAsync(() => notificationWindow?.Close()); } catch { } }); });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => HideToTray();
    private void HideToTray() { ShowInTaskbar = false; Hide(); trayIcon.ShowBalloonTip(1200, "PRomda FileShare", "Приложение продолжает работать в скрытых значках.", Forms.ToolTipIcon.Info); }
    private void RestoreFromTray() { ShowInTaskbar = true; Show(); WindowState = WindowState.Normal; Activate(); Topmost = true; Topmost = false; }
    private void ExitApplication() { allowRealClose = true; trayIcon.Visible = false; trayIcon.Dispose(); trayMenu.Dispose(); Close(); }
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private static string FormatSize(long n) { string[] u = { "Б", "КБ", "МБ", "ГБ" }; double d = n; var i = 0; while (d >= 1024 && i < u.Length - 1) { d /= 1024; i++; } return $"{d:0.##} {u[i]}"; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!allowRealClose) { e.Cancel = true; HideToTray(); return; } base.OnClosing(e); }
    protected override void OnClosed(EventArgs e) { accessDecision?.TrySetResult(false); if (clientSession != null) { clientSession.FilesUpdated -= OnRemoteFilesUpdated; clientSession.Dispose(); } server?.Stop(); notificationWindow?.Close(); if (trayIcon.Visible) trayIcon.Visible = false; trayIcon.Dispose(); trayMenu.Dispose(); accessApprovalGate.Dispose(); base.OnClosed(e); }
}

internal sealed class NotificationWindow : Window
{
    public NotificationWindow(string title, string message)
    {
        Width = 330; Height = 82; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Background = Brushes.Transparent;
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)), BorderBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14) };
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold }); panel.Children.Add(new TextBlock { Text = message, Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) }); border.Child = panel; Content = border;
        Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = area.Right - Width - 18; Top = area.Top + 18; };
    }
}

internal sealed class DownloadProgressWindow : Window
{
    private readonly TextBlock titleText; private readonly TextBlock percentText; private readonly TextBlock detailText; private readonly ProgressBar progressBar;
    public DownloadProgressWindow()
    {
        Title = "PRomda FileShare"; Width = 350; Height = 112; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual; Background = Brushes.Transparent;
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(9, 9, 9)), BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14) };
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        titleText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }; percentText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right }; var header = new Grid(); header.Children.Add(titleText); header.Children.Add(percentText); grid.Children.Add(header);
        progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 7, Margin = new Thickness(0, 10, 0, 7) }; Grid.SetRow(progressBar, 1); grid.Children.Add(progressBar); detailText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetRow(detailText, 2); grid.Children.Add(detailText); border.Child = grid; Content = border; Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = area.Right - Width - 18; Top = area.Bottom - Height - 18; };
    }
    public void SetProgress(string fileName, int index, int totalFiles, long completed, long total) { titleText.Text = $"Скачивание: {fileName}"; var percent = total > 0 ? Math.Clamp((double)completed / total * 100, 0, 100) : 0; progressBar.Value = percent; percentText.Text = $"{percent:0}%"; detailText.Text = total > 0 ? $"Файл {index} из {totalFiles} · {FormatSize(completed)} / {FormatSize(total)}" : $"Файл {index} из {totalFiles} · подготовка"; }
    public void SetCompleted(int completed, int total, int failed) { titleText.Text = "Скачивание завершено"; progressBar.Value = 100; percentText.Text = "100%"; detailText.Text = failed == 0 ? $"Готово · файлов: {completed}/{total}" : $"Готово · успешно: {completed}, ошибок: {failed}"; }
    private static string FormatSize(long n) { string[] u = { "Б", "КБ", "МБ", "ГБ" }; double d = n; var i = 0; while (d >= 1024 && i < u.Length - 1) { d /= 1024; i++; } return $"{d:0.##} {u[i]}"; }
}
