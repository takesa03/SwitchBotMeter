using System;
using System.IO;
using System.Text.Json;

namespace SwitchBotMeter.Services;

public class DeviceDataLocationSettings
{
    public string? Directory { get; set; }
}

// 別名・グラフ表示色・履歴CSVをまとめて保存するフォルダの場所を記憶する。
// このポインタ自体は exe相対の設定フォルダに置くが、実データ本体は
// ユーザーが選んだ任意のフォルダ（bin/objの削除等に巻き込まれない場所）に保存できるようにする
public class DeviceDataLocationStore
{
    private readonly string filePath;

    public DeviceDataLocationStore()
    {
        filePath = Path.Combine(AppPaths.SettingsDirectory, "device_data_location.json");
    }

    // 未設定の場合は従来通り settings フォルダ直下を使う（後方互換）
    public string Load()
    {
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<DeviceDataLocationSettings>(json);
                if (!string.IsNullOrWhiteSpace(settings?.Directory))
                {
                    return settings!.Directory!;
                }
            }
            catch
            {
                // 読み込み失敗時は既定値にフォールバック
            }
        }

        return AppPaths.SettingsDirectory;
    }

    public void Save(string directory)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, JsonSerializer.Serialize(new DeviceDataLocationSettings { Directory = directory }, options));
    }
}
