using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualBasic.FileIO;
using CtrlLauncher.Infrastructure;
using CtrlLauncher.Models;

namespace CtrlLauncher.Services;

public sealed class GameRepository
{
    private readonly AppPathService _paths;
    private readonly FileLogService _log;
    private readonly GenreCatalogService _genres;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public GameRepository(AppPathService paths, FileLogService log, GenreCatalogService genres)
    {
        _paths = paths;
        _log = log;
        _genres = genres;
    }

    public async Task<IReadOnlyList<GameEntry>> LoadAsync()
    {
        _paths.EnsureDirectories();
        var games = new List<GameEntry>();

        foreach (var directory in Directory.EnumerateDirectories(_paths.DataDirectory).OrderBy(Path.GetFileName))
        {
            var folderName = Path.GetFileName(directory);
            var settingsPath = Path.Combine(directory, "setting.json");
            GameEntry game;
            try
            {
                if (File.Exists(settingsPath))
                {
                    await using var stream = File.OpenRead(settingsPath);
                    game = await JsonSerializer.DeserializeAsync<GameEntry>(stream, _json) ?? new GameEntry();
                }
                else
                {
                    game = new GameEntry();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"{settingsPath} を読み込めませんでした。", ex);
                game = new GameEntry { Description = "setting.json を読み込めませんでした。" };
            }

            game.FolderName = folderName;
            game.GameDirectory = directory;
            game.Title = string.IsNullOrWhiteSpace(game.Title) ? folderName : game.Title;
            UpdateResolvedPaths(game);
            games.Add(game);
        }
        _genres.EnsureGenres(games.Select(game => game.Genre));
        return games;
    }

    public async Task SaveAsync(GameEntry game, string originalFolderName)
    {
        var nameError = GameFolderNameValidator.GetError(game.FolderName);
        if (nameError is not null) throw new InvalidOperationException(nameError);

        _paths.EnsureDirectories();
        var targetDirectory = Path.GetFullPath(Path.Combine(_paths.DataDirectory, game.FolderName));
        var originalDirectory = string.IsNullOrWhiteSpace(originalFolderName)
            ? targetDirectory
            : Path.GetFullPath(Path.Combine(_paths.DataDirectory, originalFolderName));
        var isRename = !string.Equals(originalDirectory, targetDirectory, StringComparison.Ordinal);
        var isSameDirectory = string.Equals(originalDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase);
        var originalSource = Path.Combine(_paths.SourceDirectory, originalFolderName);
        var targetSource = Path.Combine(_paths.SourceDirectory, game.FolderName);

        if (isRename && !isSameDirectory && Directory.Exists(targetDirectory))
            throw new IOException("同じゲーム名のフォルダーが既にあります。");
        if (isRename && Directory.Exists(originalSource) && !PathsReferToSameDirectory(originalSource, targetSource) &&
            Directory.Exists(targetSource))
            throw new IOException("変更後のゲーム名と同じソースコードフォルダーが既にあります。");

        var pathBase = Directory.Exists(originalDirectory) ? originalDirectory : targetDirectory;
        var executablePath = _paths.MakePortablePath(pathBase, game.ExecutableFullPath);
        var screenshotPath = _paths.MakePortablePath(pathBase, game.ScreenshotFullPath);
        var dataMoved = false;
        var sourceMoved = false;

        try
        {
            if (isRename && Directory.Exists(originalDirectory))
            {
                MoveDirectory(originalDirectory, targetDirectory);
                dataMoved = true;
            }
            else
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (isRename && Directory.Exists(originalSource))
            {
                MoveDirectory(originalSource, targetSource);
                sourceMoved = true;
            }

            game.GameDirectory = targetDirectory;
            game.ExecutablePath = executablePath;
            game.ScreenshotPath = screenshotPath;
            var settingsPath = Path.Combine(targetDirectory, "setting.json");
            var temporaryPath = settingsPath + ".tmp";
            try
            {
                await using (var stream = File.Create(temporaryPath))
                    await JsonSerializer.SerializeAsync(stream, game, _json);
                File.Move(temporaryPath, settingsPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            UpdateResolvedPaths(game);
            _genres.EnsureGenres([game.Genre]);
            _log.Info($"ゲーム設定を保存しました: {game.Title}");
        }
        catch (Exception saveException)
        {
            Exception? rollbackException = null;
            try
            {
                if (sourceMoved && Directory.Exists(targetSource))
                    MoveDirectory(targetSource, originalSource);
            }
            catch (Exception ex)
            {
                rollbackException = ex;
                _log.Error("ソースコードフォルダー名を元に戻せませんでした。", ex);
            }

            try
            {
                if (dataMoved && Directory.Exists(targetDirectory))
                    MoveDirectory(targetDirectory, originalDirectory);
            }
            catch (Exception ex)
            {
                rollbackException = rollbackException is null ? ex : new AggregateException(rollbackException, ex);
                _log.Error("ゲームデータフォルダー名を元に戻せませんでした。", ex);
            }

            if (rollbackException is not null)
                throw new AggregateException("保存に失敗し、フォルダー名も完全には元に戻せませんでした。", saveException, rollbackException);
            throw;
        }
    }

    public async Task DeleteAsync(string folderName)
    {
        var nameError = GameFolderNameValidator.GetError(folderName);
        if (nameError is not null) throw new InvalidOperationException(nameError);

        var directory = Path.GetFullPath(Path.Combine(_paths.DataDirectory, folderName));
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("削除するゲームフォルダーが見つかりません。");

        await Task.Run(() => FileSystem.DeleteDirectory(directory, UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin));
        _log.Info($"ゲームフォルダーをごみ箱へ移動しました: {folderName}");
    }

    private void UpdateResolvedPaths(GameEntry game)
    {
        game.ExecutableFullPath = _paths.ResolveGamePath(game.GameDirectory, game.ExecutablePath);
        game.ScreenshotFullPath = _paths.ResolveGamePath(game.GameDirectory, game.ScreenshotPath);
    }

    private static bool PathsReferToSameDirectory(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static void MoveDirectory(string source, string destination)
    {
        if (string.Equals(source, destination, StringComparison.Ordinal)) return;
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            var temporary = Path.Combine(Path.GetDirectoryName(source)!, $".rename-{Guid.NewGuid():N}");
            Directory.Move(source, temporary);
            try { Directory.Move(temporary, destination); }
            catch
            {
                if (Directory.Exists(temporary) && !Directory.Exists(source)) Directory.Move(temporary, source);
                throw;
            }
            return;
        }
        Directory.Move(source, destination);
    }
}
