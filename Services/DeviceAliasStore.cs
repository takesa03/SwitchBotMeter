using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SwitchBotMeter.Services;

public class DeviceAliasStore
{
    private readonly string filePath;
    private Dictionary<ulong, string> aliases = new();

    public DeviceAliasStore()
    {
        filePath = Path.Combine(AppPaths.SettingsDirectory, "device_aliases.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var json = File.ReadAllText(filePath);
            aliases = JsonSerializer.Deserialize<Dictionary<ulong, string>>(json) ?? new();
        }
        catch
        {
            aliases = new();
        }
    }

    public string? GetAlias(ulong address) => aliases.TryGetValue(address, out var alias) ? alias : null;

    public void SetAlias(ulong address, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            aliases.Remove(address);
        }
        else
        {
            aliases[address] = alias;
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, JsonSerializer.Serialize(aliases, options));
    }
}
