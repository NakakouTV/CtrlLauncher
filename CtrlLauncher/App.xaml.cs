using System.Windows;
using System.Windows.Input;
using CtrlLauncher.Services;
using CtrlLauncher.ViewModels;

namespace CtrlLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = new AppPathService();
        var log = new FileLogService(paths);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.Error("回復不能なエラーが発生しました。", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            log.Error("UIで処理されていないエラーが発生しました。", args.Exception);
            MessageBox.Show("予期しないエラーが発生しました。詳細は logs フォルダーを確認してください。",
                "CtrlLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var genres = new GenreCatalogService(paths, log);
        var repository = new GameRepository(paths, log, genres);
        var settings = new LauncherSettingsService(paths, log);
        var gameRunner = new GameRunner(log);
        var desktopSession = new DesktopSessionService(log);
        var dialogService = new DialogService(repository, paths, log, genres);
        var isDesktopChild = e.Args.Contains("--desktop-child", StringComparer.OrdinalIgnoreCase);
        var viewModel = new MainViewModel(repository, settings, gameRunner, desktopSession, dialogService, genres, log, isDesktopChild);

        var window = new MainWindow { DataContext = viewModel };
        if (isDesktopChild)
        {
            window.WindowStyle = WindowStyle.None;
            window.WindowState = WindowState.Maximized;
            window.ResizeMode = ResizeMode.NoResize;
        }
        MainWindow = window;
        EventWaitHandle? readyEvent = null;
        var readyEventName = GetArgumentValue(e.Args, "--ready-event");
        if (isDesktopChild && !string.IsNullOrWhiteSpace(readyEventName))
        {
            try { readyEvent = EventWaitHandle.OpenExisting(readyEventName); }
            catch (WaitHandleCannotBeOpenedException ex) { log.Error("展示モードの準備完了通知を開けませんでした。", ex); }
        }
        window.ContentRendered += (_, _) =>
        {
            if (readyEvent is null) return;
            readyEvent.Set();
            readyEvent.Dispose();
            readyEvent = null;
            log.Info("展示用ランチャーの画面準備が完了しました。");
        };
        window.Show();
        _ = viewModel.InitializeAsync();
    }

    private static string? GetArgumentValue(string[] arguments, string name)
    {
        var index = Array.FindIndex(arguments, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
}
