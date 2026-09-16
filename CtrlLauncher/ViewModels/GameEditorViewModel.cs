using Microsoft.Win32;
using System.Collections.ObjectModel;
using CtrlLauncher.Infrastructure;
using CtrlLauncher.Models;
using CtrlLauncher.Services;

namespace CtrlLauncher.ViewModels;

public sealed class GameEditorViewModel : ObservableObject
{
    private readonly GameRepository _repository;
    private readonly AppPathService _paths;
    private readonly FileLogService _log;
    private readonly GenreCatalogService _genres;
    private readonly DialogService _dialogs;
    private readonly GameEntry _game;
    private readonly string _originalFolderName;
    private string _gameName;
    private string _genre;
    private string _description;
    private string _executablePath;
    private string _screenshotPath;
    private Difficulty _minDifficulty;
    private Difficulty _maxDifficulty;
    private bool _difficultyEnabled;
    private string _validationMessage = string.Empty;

    public GameEditorViewModel(GameRepository repository, AppPathService paths, FileLogService log,
        GenreCatalogService genres, DialogService dialogs, GameEntry? source)
    {
        _repository = repository;
        _paths = paths;
        _log = log;
        _genres = genres;
        _dialogs = dialogs;
        IsNew = source is null;
        _game = source?.Copy() ?? new GameEntry();
        _originalFolderName = _game.FolderName;
        _gameName = _game.Title;
        _genre = _game.Genre;
        _description = _game.Description;
        _executablePath = _game.ExecutableFullPath;
        _screenshotPath = _game.ScreenshotFullPath;
        _minDifficulty = _game.MinDifficulty == Difficulty.Default ? Difficulty.Easy : _game.MinDifficulty;
        _maxDifficulty = _game.MaxDifficulty == Difficulty.Default ? Difficulty.Hard : _game.MaxDifficulty;
        _difficultyEnabled = _game.HasDifficulty;
        foreach (var genre in _genres.Load()) GenreChoices.Add(genre);

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => GetGameNameError() is null);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
        BrowseExecutableCommand = new RelayCommand(BrowseExecutable);
        BrowseScreenshotCommand = new RelayCommand(BrowseScreenshot);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !IsNew);
        AddGenreCommand = new RelayCommand(AddGenre, CanAddGenre);
        RemoveGenreCommand = new RelayCommand(RemoveGenre, CanRemoveGenre);
        UpdateGameNameValidation();
    }

    public event Action<bool>? CloseRequested;
    public bool IsNew { get; }
    public string SaveButtonText => IsNew ? "追加" : "保存";
    public IReadOnlyList<DifficultyChoice> DifficultyChoices { get; } =
    [
        new(Difficulty.Easy, "かんたん"),
        new(Difficulty.Normal, "ふつう"),
        new(Difficulty.Hard, "むずかしい")
    ];
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseExecutableCommand { get; }
    public RelayCommand BrowseScreenshotCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand AddGenreCommand { get; }
    public RelayCommand RemoveGenreCommand { get; }
    public bool CanDelete => !IsNew;
    public ObservableCollection<string> GenreChoices { get; } = [];

    public string GameName
    {
        get => _gameName;
        set
        {
            if (!SetProperty(ref _gameName, value)) return;
            UpdateGameNameValidation();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
    public string Genre
    {
        get => _genre;
        set
        {
            if (!SetProperty(ref _genre, value)) return;
            AddGenreCommand.NotifyCanExecuteChanged();
            RemoveGenreCommand.NotifyCanExecuteChanged();
        }
    }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string ExecutablePath { get => _executablePath; set => SetProperty(ref _executablePath, value); }
    public string ScreenshotPath { get => _screenshotPath; set => SetProperty(ref _screenshotPath, value); }
    public Difficulty MinDifficulty { get => _minDifficulty; set => SetProperty(ref _minDifficulty, value); }
    public Difficulty MaxDifficulty { get => _maxDifficulty; set => SetProperty(ref _maxDifficulty, value); }
    public bool DifficultyEnabled { get => _difficultyEnabled; set => SetProperty(ref _difficultyEnabled, value); }
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    private void BrowseExecutable()
    {
        var dialog = new OpenFileDialog { Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            ExecutablePath = dialog.FileName;
            GameName = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void BrowseScreenshot()
    {
        var dialog = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.webp|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog() == true) ScreenshotPath = dialog.FileName;
    }

    private async Task SaveAsync()
    {
        ValidationMessage = Validate();
        if (ValidationMessage.Length > 0) return;

        try
        {
            _game.FolderName = GameName;
            _game.Title = GameName;
            _game.Genre = Genre.Trim();
            _game.Description = Description.Trim();
            _game.ExecutableFullPath = ExecutablePath.Trim();
            _game.ScreenshotFullPath = ScreenshotPath.Trim();
            _game.MinDifficulty = DifficultyEnabled ? MinDifficulty : Difficulty.Default;
            _game.MaxDifficulty = DifficultyEnabled ? MaxDifficulty : Difficulty.Default;
            await _repository.SaveAsync(_game, _originalFolderName);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            _log.Error("ゲーム設定を保存できませんでした。", ex);
            ValidationMessage = ex.Message;
        }
    }

    private bool CanAddGenre() => !string.IsNullOrWhiteSpace(Genre) &&
        !GenreChoices.Contains(Genre.Trim(), StringComparer.OrdinalIgnoreCase);

    private void AddGenre()
    {
        var genre = Genre.Trim();
        if (genre.Length == 0) return;
        _genres.Add(genre);
        ReloadGenres();
        Genre = genre;
    }

    private bool CanRemoveGenre() => !string.IsNullOrWhiteSpace(Genre) &&
        GenreChoices.Contains(Genre.Trim(), StringComparer.OrdinalIgnoreCase);

    private void RemoveGenre()
    {
        var genre = Genre.Trim();
        if (genre.Length == 0) return;
        _genres.Remove(genre);
        Genre = string.Empty;
        ReloadGenres();
    }

    private void ReloadGenres()
    {
        GenreChoices.Clear();
        foreach (var genre in _genres.Load()) GenreChoices.Add(genre);
        AddGenreCommand.NotifyCanExecuteChanged();
        RemoveGenreCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteAsync()
    {
        if (IsNew || !_dialogs.ConfirmGameDeletion(_game.Title)) return;

        try
        {
            await _repository.DeleteAsync(_originalFolderName);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            _log.Error("ゲームを削除できませんでした。", ex);
            ValidationMessage = ex.Message;
        }
    }

    private string Validate()
    {
        var nameError = GetGameNameError();
        if (nameError is not null) return nameError;
        if (string.IsNullOrWhiteSpace(ExecutablePath) || !File.Exists(ExecutablePath)) return "有効な実行ファイルを選択してください。";
        if (!string.IsNullOrWhiteSpace(ScreenshotPath) && !File.Exists(ScreenshotPath)) return "画像ファイルが見つかりません。";
        if (DifficultyEnabled && MinDifficulty > MaxDifficulty) return "最低難易度は最高難易度以下にしてください。";
        return string.Empty;
    }

    private string? GetGameNameError()
    {
        var error = GameFolderNameValidator.GetError(GameName);
        if (error is not null) return error;

        var desiredPath = Path.GetFullPath(Path.Combine(_paths.DataDirectory, GameName));
        var originalPath = string.IsNullOrEmpty(_originalFolderName)
            ? string.Empty
            : Path.GetFullPath(Path.Combine(_paths.DataDirectory, _originalFolderName));
        if (Directory.Exists(desiredPath) &&
            !string.Equals(desiredPath, originalPath, StringComparison.OrdinalIgnoreCase))
            return "同じゲーム名のフォルダーが既にあります。";
        return null;
    }

    private void UpdateGameNameValidation() => ValidationMessage = GetGameNameError() ?? string.Empty;

    public sealed record DifficultyChoice(Difficulty Value, string Label);
}
