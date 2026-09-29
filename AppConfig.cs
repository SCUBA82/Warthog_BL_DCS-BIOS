using System.Text.Json;

namespace WarthogLedControl;

public class AppConfig
{
    public DcsBiosConfig DcsBios { get; set; } = new();
    public BacklightConfig Backlight { get; set; } = new();

    public static AppConfig Load(string fileName)
    {
        if (!File.Exists(fileName))
            throw new FileNotFoundException(
                $"Konfigurationsdatei nicht gefunden: {fileName}");

        string json = File.ReadAllText(fileName);

        var config = JsonSerializer.Deserialize<AppConfig>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (config == null)
            throw new Exception(
                "Die Konfigurationsdatei konnte nicht gelesen werden.");

        return config;
    }
}

public class DcsBiosConfig
{
    public string Address { get; set; } = "0x7560";

    public ushort GetAddress()
    {
        string value = Address.Trim();

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value[2..];

        return Convert.ToUInt16(value, 16);
    }
}

public class BacklightConfig
{
    public List<BacklightLevel> Levels { get; set; } = new();
}

public class BacklightLevel
{
    public int Level { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }

    public bool Contains(ushort value)
    {
        return value >= Min && value <= Max;
    }
}
