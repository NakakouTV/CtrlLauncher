namespace CtrlLauncher.Services;

public sealed class LauncherSettingsService(AppPathService paths, FileLogService log)
{
    public async Task<TimeSpan> GetPlayTimeAsync()
    {
        paths.EnsureDirectories();
        if (!File.Exists(paths.TimeSettingsPath))
        {
            await File.WriteAllTextAsync(paths.TimeSettingsPath, "30");
            return TimeSpan.FromMinutes(30);
        }

        try
        {
            var text = await File.ReadAllTextAsync(paths.TimeSettingsPath);
            if (int.TryParse(text.Trim(), out var minutes) && minutes > 0)
                return TimeSpan.FromMinutes(minutes);
        }
        catch (Exception ex)
        {
            log.Error("time.txt を読み込めませんでした。", ex);
        }
        return TimeSpan.FromMinutes(30);
    }
}
