using System.Text.Json;

namespace CtrlLauncher.Services;

public sealed class GenreCatalogService(AppPathService paths, FileLogService log)
{
    private static readonly string[] DefaultGenres =
        ["アクション", "シューティング", "パズル", "アドベンチャー", "RPG", "レース", "その他"];
    private readonly object _sync = new();

    public IReadOnlyList<string> Load()
    {
        lock (_sync)
        {
            paths.EnsureDirectories();
            if (!File.Exists(paths.GenreSettingsPath))
            {
                SaveCore(DefaultGenres);
                return DefaultGenres;
            }

            try
            {
                var genres = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(paths.GenreSettingsPath));
                return Normalize(genres ?? []).ToArray();
            }
            catch (Exception ex)
            {
                log.Error("genres.json を読み込めませんでした。", ex);
                return DefaultGenres;
            }
        }
    }

    public void EnsureGenres(IEnumerable<string> genres)
    {
        lock (_sync)
        {
            var current = Load().ToList();
            var changed = false;
            foreach (var genre in Normalize(genres))
            {
                if (current.Contains(genre, StringComparer.OrdinalIgnoreCase)) continue;
                current.Add(genre);
                changed = true;
            }
            if (changed) SaveCore(current);
        }
    }

    public void Add(string genre) => EnsureGenres([genre]);

    public void Remove(string genre)
    {
        lock (_sync)
        {
            var current = Load().Where(value => !string.Equals(value, genre.Trim(), StringComparison.OrdinalIgnoreCase));
            SaveCore(current);
        }
    }

    private void SaveCore(IEnumerable<string> genres)
    {
        paths.EnsureDirectories();
        var json = JsonSerializer.Serialize(Normalize(genres), new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = paths.GenreSettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, paths.GenreSettingsPath, true);
    }

    private static IEnumerable<string> Normalize(IEnumerable<string> genres) => genres
        .Select(value => value.Trim())
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase);
}
