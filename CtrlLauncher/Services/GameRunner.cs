using System.Diagnostics;
using CtrlLauncher.Models;

namespace CtrlLauncher.Services;

public sealed class GameRunner(FileLogService log)
{
    public Process Start(GameEntry game)
    {
        if (!File.Exists(game.ExecutableFullPath))
            throw new FileNotFoundException("ゲームの実行ファイルが見つかりません。", game.ExecutableFullPath);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = game.ExecutableFullPath,
            WorkingDirectory = Path.GetDirectoryName(game.ExecutableFullPath)!,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("ゲームを起動できませんでした。");
        log.Info($"ゲームを起動しました: {game.Title} (PID {process.Id})");
        return process;
    }

}
