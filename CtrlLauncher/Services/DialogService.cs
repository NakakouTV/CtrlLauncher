using System.Windows;
using CtrlLauncher.Models;
using CtrlLauncher.ViewModels;
using CtrlLauncher.Views;

namespace CtrlLauncher.Services;

public sealed class DialogService(GameRepository repository, AppPathService paths, FileLogService log,
    GenreCatalogService genres)
{
    public bool EditGame(GameEntry? source)
    {
        var viewModel = new GameEditorViewModel(repository, paths, log, genres, this, source);
        var window = new GameEditorWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = viewModel
        };
        viewModel.CloseRequested += result => window.DialogResult = result;
        return window.ShowDialog() == true;
    }

    public void ShowError(string message) => MessageBox.Show(Application.Current.MainWindow, message,
        "CtrlLauncher", MessageBoxButton.OK, MessageBoxImage.Error);

    public bool ConfirmGameDeletion(string gameName) => MessageBox.Show(Application.Current.MainWindow,
        $"「{gameName}」を削除しますか？\n\ndata\\{gameName} フォルダー内のゲーム本体、画像、設定をすべてごみ箱へ移動します。",
        "ゲームの削除", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
