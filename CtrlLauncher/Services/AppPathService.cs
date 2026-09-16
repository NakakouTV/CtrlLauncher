namespace CtrlLauncher.Services;

public sealed class AppPathService
{
    public string RootDirectory { get; } = FindRootDirectory();
    public string DataDirectory => Path.Combine(RootDirectory, "data");
    public string SourceDirectory => Path.Combine(RootDirectory, "src");
    public string LogDirectory => Path.Combine(RootDirectory, "logs");
    public string TimeSettingsPath => Path.Combine(RootDirectory, "time.txt");
    public string GenreSettingsPath => Path.Combine(RootDirectory, "genres.json");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(SourceDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string ResolveGamePath(string gameDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

        var fromGame = Path.GetFullPath(Path.Combine(gameDirectory, path));
        if (File.Exists(fromGame) || Directory.Exists(fromGame)) return fromGame;
        return Path.GetFullPath(Path.Combine(RootDirectory, path));
    }

    public string MakePortablePath(string gameDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(gameDirectory, fullPath);
        return IsOutside(relative) ? fullPath : relative;
    }

    private static bool IsOutside(string relativePath) =>
        relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

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
