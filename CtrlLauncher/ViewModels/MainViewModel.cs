using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CtrlLauncher.Infrastructure;
using CtrlLauncher.Models;
using CtrlLauncher.Services;

namespace CtrlLauncher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly GameRepository _repository;
    private readonly LauncherSettingsService _settings;
    private readonly GameRunner _gameRunner;
    private readonly DesktopSessionService _desktopSession;
    private readonly DialogService _dialogs;
    private readonly GenreCatalogService _genres;
    private readonly FileLogService _log;
    private GameEntry? _selectedGame;
    private bool _isAdminMode;
    private bool _isBusy;
    private bool _timeExpired;
    private string _statusText = "ゲームを選んでください";
    private string _adminBuffer = string.Empty;
    private Process? _runningProcess;
    private TaskCompletionSource<TimeDecision>? _timeDecision;
    private string _selectedGenreFilter = AllGenresLabel;
    private DifficultyFilterChoice _selectedDifficultyFilter;
    private const string AllGenresLabel = "すべてのジャンル";
    private const string RepositoryUrl = "https://github.com/NakakouTV/CtrlLauncher";

    public MainViewModel(GameRepository repository, LauncherSettingsService settings, GameRunner gameRunner,
        DesktopSessionService desktopSession, DialogService dialogs, GenreCatalogService genres,
        FileLogService log, bool isExhibitionSession)
    {
        _repository = repository;
        _settings = settings;
        _gameRunner = gameRunner;
        _desktopSession = desktopSession;
        _dialogs = dialogs;
        _genres = genres;
        _log = log;
        IsExhibitionSession = isExhibitionSession;
        DifficultyFilterChoices =
        [
            new(null, "すべての難易度"),
            new(Difficulty.Default, "指定なし"),
            new(Difficulty.Easy, "かんたん"),
            new(Difficulty.Normal, "ふつう"),
            new(Difficulty.Hard, "むずかしい")
        ];
        _selectedDifficultyFilter = DifficultyFilterChoices[0];
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        StartGameCommand = new AsyncRelayCommand(StartGameAsync, () => SelectedGame is not null && !IsBusy);
        AddGameCommand = new RelayCommand(AddGame, () => IsAdminMode && !IsBusy);
        EditGameCommand = new RelayCommand(EditGame, () => IsAdminMode && SelectedGame is not null && !IsBusy);
        ExhibitionModeCommand = new AsyncRelayCommand(StartExhibitionModeAsync,
            () => IsAdminMode && !IsBusy && !IsExhibitionSession);
        ContinueCommand = new RelayCommand(() => ResolveTimeDecision(TimeDecision.Continue));
        ChangePlayerCommand = new RelayCommand(() => ResolveTimeDecision(TimeDecision.ChangePlayer));
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(), () => IsAdminMode && !IsBusy);
        OpenRepositoryCommand = new RelayCommand(OpenRepository, () => IsAdminMode);
    }

    public event Action? ExitRequested;
    public event Action<bool>? LauncherVisibilityRequested;
    public ObservableCollection<GameEntry> Games { get; } = [];
    public ICollectionView GamesView { get; }
    public ObservableCollection<string> GenreFilterChoices { get; } = [];
    public IReadOnlyList<DifficultyFilterChoice> DifficultyFilterChoices { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand StartGameCommand { get; }
    public RelayCommand AddGameCommand { get; }
    public RelayCommand EditGameCommand { get; }
    public AsyncRelayCommand ExhibitionModeCommand { get; }
    public RelayCommand ContinueCommand { get; }
    public RelayCommand ChangePlayerCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand OpenRepositoryCommand { get; }
    public bool IsExhibitionSession { get; }

    public GameEntry? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (!SetProperty(ref _selectedGame, value)) return;
            StatusText = value is null ? "ゲームを選んでください" : value.Title;
            NotifyCommands();
        }
    }
    public bool IsAdminMode { get => _isAdminMode; private set { if (SetProperty(ref _isAdminMode, value)) NotifyCommands(); } }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyCommands(); } }
    public bool TimeExpired { get => _timeExpired; private set => SetProperty(ref _timeExpired, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string SelectedGenreFilter
    {
        get => _selectedGenreFilter;
        set
        {
            if (SetProperty(ref _selectedGenreFilter, value)) GamesView.Refresh();
        }
    }
    public DifficultyFilterChoice SelectedDifficultyFilter
    {
        get => _selectedDifficultyFilter;
        set
        {
            if (SetProperty(ref _selectedDifficultyFilter, value)) GamesView.Refresh();
        }
    }

    public Task InitializeAsync() => RefreshAsync();

    public void AcceptAdminKey(char? letter)
    {
        if (IsAdminMode) return;
        if (letter is null)
        {
            _adminBuffer = string.Empty;
            return;
        }
        _adminBuffer += letter.Value;
        if (_adminBuffer.Length > 4) _adminBuffer = _adminBuffer[^4..];
        if (_adminBuffer == "CTRL")
        {
            IsAdminMode = true;
            StatusText = "管理モードになりました";
            _adminBuffer = string.Empty;
            _ = RefreshAsync();
        }
    }

    public void StopGameFromAltTab()
    {
        if (!IsExhibitionSession || _runningProcess is null || _runningProcess.HasExited)
            return;

        try
        {
            StatusText = "Alt+Tabによりゲームを終了しています";
            _runningProcess.Kill(entireProcessTree: true);
            _log.Info("Alt+Tabにより実行中のゲームを終了しました。");
        }
        catch (Exception ex)
        {
            _log.Error("Alt+Tabによるゲーム終了に失敗しました。", ex);
        }
    }

    private async Task RefreshAsync()
    {
        var selectedFolder = SelectedGame?.FolderName;
        try
        {
            IsBusy = true;
            var games = await _repository.LoadAsync();
            Games.Clear();
            foreach (var game in games) Games.Add(game);
            RefreshGenreFilters();
            GamesView.Refresh();
            SelectedGame = Games.FirstOrDefault(x => x.FolderName == selectedFolder) ?? Games.FirstOrDefault();
            StatusText = Games.Count == 0 ? "管理モードでゲームを登録してください" : $"{Games.Count}件のゲームを読み込みました";
        }
        catch (Exception ex)
        {
            _log.Error("ゲーム一覧を読み込めませんでした。", ex);
            _dialogs.ShowError(ex.Message);
        }
        finally { IsBusy = false; }
    }

    private async Task StartGameAsync()
    {
        if (SelectedGame is null) return;
        try
        {
            IsBusy = true;
            StatusText = $"{SelectedGame.Title} を実行中";
            _runningProcess = _gameRunner.Start(SelectedGame);
            SetLauncherVisible(false);
            var playTime = await _settings.GetPlayTimeAsync();
            var exitTask = _runningProcess.WaitForExitAsync();

            while (!_runningProcess.HasExited)
            {
                var finished = await Task.WhenAny(exitTask, Task.Delay(playTime));
                if (finished == exitTask) break;
                TimeExpired = true;
                SetLauncherVisible(true);
                _timeDecision = new TaskCompletionSource<TimeDecision>();
                var decision = await _timeDecision.Task;
                TimeExpired = false;
                if (decision == TimeDecision.ChangePlayer)
                {
                    if (!_runningProcess.HasExited) _runningProcess.Kill(entireProcessTree: true);
                    break;
                }
                SetLauncherVisible(false);
            }
            await exitTask;
            StatusText = $"{SelectedGame.Title} が終了しました";
            _log.Info($"ゲームが終了しました: {SelectedGame.Title}");
        }
        catch (Exception ex)
        {
            SetLauncherVisible(true);
            _log.Error("ゲームの実行中にエラーが発生しました。", ex);
            _dialogs.ShowError(ex.Message);
            StatusText = "ゲームを起動できませんでした";
        }
        finally
        {
            SetLauncherVisible(true);
            TimeExpired = false;
            _runningProcess?.Dispose();
            _runningProcess = null;
            IsBusy = false;
        }
    }

    private void AddGame() { _dialogs.EditGame(null); _ = RefreshAsync(); }
    private void EditGame() { if (SelectedGame is not null) { _dialogs.EditGame(SelectedGame); _ = RefreshAsync(); } }

    private async Task StartExhibitionModeAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "展示モードを開始しています";
            await RefreshAsync();
            await _desktopSession.RunAsync();
            await RefreshAsync();
            StatusText = "展示モードから戻りました";
        }
        catch (Exception ex)
        {
            _log.Error("展示モードを開始できませんでした。", ex);
            _dialogs.ShowError(ex.Message);
            StatusText = "展示モードを開始できませんでした";
        }
        finally { IsBusy = false; }
    }

    private void ResolveTimeDecision(TimeDecision decision) => _timeDecision?.TrySetResult(decision);

    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error("GitHubを開けませんでした。", ex);
            _dialogs.ShowError("GitHubを開けませんでした。");
        }
    }

    private void SetLauncherVisible(bool visible)
    {
        if (IsExhibitionSession)
            LauncherVisibilityRequested?.Invoke(visible);
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        StartGameCommand.NotifyCanExecuteChanged();
        AddGameCommand.NotifyCanExecuteChanged();
        EditGameCommand.NotifyCanExecuteChanged();
        ExhibitionModeCommand.NotifyCanExecuteChanged();
        ExitCommand.NotifyCanExecuteChanged();
        OpenRepositoryCommand.NotifyCanExecuteChanged();
    }

    private void RefreshGenreFilters()
    {
        var previous = SelectedGenreFilter;
        GenreFilterChoices.Clear();
        GenreFilterChoices.Add(AllGenresLabel);
        foreach (var genre in _genres.Load()) GenreFilterChoices.Add(genre);
        SelectedGenreFilter = GenreFilterChoices.Contains(previous, StringComparer.OrdinalIgnoreCase)
            ? GenreFilterChoices.First(value => string.Equals(value, previous, StringComparison.OrdinalIgnoreCase))
            : AllGenresLabel;
    }

    private bool FilterGame(object item)
    {
        if (item is not GameEntry game) return false;
        if (SelectedGenreFilter != AllGenresLabel &&
            !string.Equals(game.Genre, SelectedGenreFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        var difficulty = SelectedDifficultyFilter.Value;
        if (difficulty is null) return true;
        if (difficulty == Difficulty.Default) return !game.HasDifficulty;
        return game.HasDifficulty && game.MinDifficulty <= difficulty && game.MaxDifficulty >= difficulty;
    }

    private enum TimeDecision { Continue, ChangePlayer }
    public sealed record DifficultyFilterChoice(Difficulty? Value, string Label);
}
