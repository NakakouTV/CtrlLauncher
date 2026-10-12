namespace CtrlLauncher.Services;

public sealed class AppPathService
{
    public string RootDirectory { get; } = FindRootDirectory();
    public string DataDirectory => Path.Combine(RootDirectory, "data");
    public string GamesDirectory => Path.Combine(RootDirectory, "games");
    public string SourceDirectory => Path.Combine(RootDirectory, "src");
    public string LogDirectory => Path.Combine(RootDirectory, "logs");
    public string TimeSettingsPath => Path.Combine(RootDirectory, "time.txt");
    public string GenreSettingsPath => Path.Combine(RootDirectory, "genres.json");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(GamesDirectory);
        Directory.CreateDirectory(SourceDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string ResolveGamePath(string gameDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

        var fromRoot = Path.GetFullPath(Path.Combine(RootDirectory, path));
        if (File.Exists(fromRoot) || Directory.Exists(fromRoot)) return fromRoot;
        var fromGame = Path.GetFullPath(Path.Combine(gameDirectory, path));
        return fromGame;
    }

    public string MakePortablePath(string gameDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var fullPath = Path.GetFullPath(path);
        return Path.GetRelativePath(RootDirectory, fullPath);
    }

    private static string FindRootDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CTRL_LAUNCHER_HOME");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

#if DEBUG
        // Visual Studioからのデバッグ中は、bin配下ではなくプロジェクト直下のデータを使う。
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CtrlLauncher.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
#endif
        return Path.GetFullPath(AppContext.BaseDirectory);
    }
}
