using System.Text.Json.Serialization;

namespace CtrlLauncher.Models;

public sealed class GameEntry
{
    public string Title { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ScreenshotPath { get; set; } = string.Empty;
    public Difficulty MinDifficulty { get; set; } = Difficulty.Default;
    public Difficulty MaxDifficulty { get; set; } = Difficulty.Default;
    [JsonIgnore] public string FolderName { get; set; } = string.Empty;
    [JsonIgnore] public string GameDirectory { get; set; } = string.Empty;
    [JsonIgnore] public string ExecutableFullPath { get; set; } = string.Empty;
    [JsonIgnore] public string ScreenshotFullPath { get; set; } = string.Empty;
    [JsonIgnore] public bool HasScreenshot => File.Exists(ScreenshotFullPath);
    [JsonIgnore] public bool HasDifficulty => MinDifficulty != Difficulty.Default || MaxDifficulty != Difficulty.Default;
    [JsonIgnore] public string DifficultyText => HasDifficulty
        ? $"{ToJapanese(MinDifficulty)} ～ {ToJapanese(MaxDifficulty)}" : "指定なし";
    public GameEntry Copy() => (GameEntry)MemberwiseClone();
    private static string ToJapanese(Difficulty value) => value switch
    {
        Difficulty.Easy => "かんたん", Difficulty.Normal => "ふつう", Difficulty.Hard => "むずかしい", _ => "指定なし"
    };
}
