using System;
using System.IO;

namespace SwitchBotMeter.Services;

public static class AppPaths
{
    // %AppData% はパッケージ仮想化(AppContainer)の対象になり、呼び出し元プロセスによって
    // 実体が変わってしまうことがあるため、exe と同じ場所に設定を保存する。
    // これにより、どのプロセスから起動しても常に同じファイルを参照できる。
    public static string SettingsDirectory
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "settings");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
