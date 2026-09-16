using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using CtrlLauncher.Services;
using CtrlLauncher.ViewModels;

namespace CtrlLauncher;

public partial class MainWindow : Window
{
    private bool _closeRequested;
    private ExhibitionKeyboardHook? _keyboardHook;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        SourceInitialized += OnSourceInitialized;
        if (Application.Current is { } application)
            application.SessionEnding += (_, _) => _closeRequested = true;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldViewModel)
        {
            oldViewModel.ExitRequested -= OnExitRequested;
            oldViewModel.LauncherVisibilityRequested -= OnLauncherVisibilityRequested;
        }
        if (e.NewValue is MainViewModel newViewModel)
        {
            newViewModel.ExitRequested += OnExitRequested;
            newViewModel.LauncherVisibilityRequested += OnLauncherVisibilityRequested;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.IsAdminMode)
            return;

        var letter = e.Key switch
        {
            Key.C => 'C',
            Key.T => 'T',
            Key.R => 'R',
            Key.L => 'L',
            _ => (char?)null
        };
        viewModel.AcceptAdminKey(letter);
        if (letter is not null)
            e.Handled = true;
    }

    private void OnExitRequested()
    {
        _closeRequested = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel { IsExhibitionSession: true })
        {
            _keyboardHook = new ExhibitionKeyboardHook(
                () => IsVisible && DataContext is MainViewModel { IsAdminMode: false },
                letter => (DataContext as MainViewModel)?.AcceptAdminKey(letter),
                () => Dispatcher.BeginInvoke(() =>
                    (DataContext as MainViewModel)?.StopGameFromAltTab()));
            _keyboardHook.Install();
        }
    }


    private void OnLauncherVisibilityRequested(bool visible)
    {
        if (visible)
        {
            Show();
            Topmost = true;
            Activate();
        }
        else
        {
            Topmost = false;
            Hide();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel { IsExhibitionSession: true } && !_closeRequested)
            e.Cancel = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _keyboardHook?.Dispose();
    }
}
