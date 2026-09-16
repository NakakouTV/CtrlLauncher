namespace CtrlLauncher.Infrastructure;

public static class GameFolderNameValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string? GetError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "ゲーム名を入力してください。";
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            return "ゲーム名の先頭や末尾に空白は使用できません。";
        if (name.EndsWith('.') || name.EndsWith(' '))
            return "ゲーム名の末尾にピリオドや空白は使用できません。";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "ゲーム名にフォルダー名として使用できない文字が含まれています。";
        if (name is "." or "..")
            return "このゲーム名は使用できません。";

        var deviceName = name.Split('.')[0];
        if (ReservedNames.Contains(deviceName))
            return $"「{deviceName}」はWindowsで予約されているため使用できません。";
        if (name.Length > 100)
            return "ゲーム名は100文字以内にしてください。";
        return null;
    }
}
