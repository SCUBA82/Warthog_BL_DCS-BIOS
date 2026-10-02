using System.Globalization;
using System.Text.Json;

namespace WarthogLedControl;

public class AppConfig
{
    public DcsBiosConfig DcsBios { get; set; } = new();

    public BacklightConfig Backlight { get; set; } = new();


    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };


    // ====================================================================
    // CONFIG LADEN
    // ====================================================================

    public static AppConfig Load(
        string fileName)
    {
        // ------------------------------------------------------------
        // Wenn die Datei noch nicht existiert:
        // Standardkonfiguration erstellen und speichern.
        // ------------------------------------------------------------

        if (!File.Exists(fileName))
        {
            var config = CreateDefault();

            config.Save(fileName);

            return config;
        }


        // ------------------------------------------------------------
        // Datei lesen
        // ------------------------------------------------------------

        string json =
            File.ReadAllText(fileName);


        var configFromFile =
            JsonSerializer.Deserialize<AppConfig>(
                json,
                JsonOptions);


        if (configFromFile == null)
        {
            throw new InvalidOperationException(
                "Die Konfigurationsdatei konnte nicht gelesen werden.");
        }


        // ------------------------------------------------------------
        // Sicherheitshalber fehlende Bereiche initialisieren
        // ------------------------------------------------------------

        configFromFile.DcsBios ??=
            new DcsBiosConfig();

        configFromFile.Backlight ??=
            new BacklightConfig();

        configFromFile.Backlight.Levels ??=
            new List<BacklightLevel>();


        return configFromFile;
    }


    // ====================================================================
    // CONFIG SPEICHERN
    // ====================================================================

    public void Save(
        string fileName)
    {
        string json =
            JsonSerializer.Serialize(
                this,
                JsonOptions);


        File.WriteAllText(
            fileName,
            json);
    }


    // ====================================================================
    // STANDARDKONFIGURATION
    // ====================================================================

    private static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            DcsBios = new DcsBiosConfig
            {
                Address = "0x7560"
            },

            Backlight = new BacklightConfig
            {
                Levels = new List<BacklightLevel>
                {
                    new BacklightLevel
                    {
                        Level = 0,
                        Min = 0,
                        Max = 0
                    },

                    new BacklightLevel
                    {
                        Level = 1,
                        Min = 1,
                        Max = 19999
                    },

                    new BacklightLevel
                    {
                        Level = 2,
                        Min = 20000,
                        Max = 40000
                    },

                    new BacklightLevel
                    {
                        Level = 3,
                        Min = 40001,
                        Max = 65535
                    }
                }
            }
        };
    }
}


// ========================================================================
// DCS-BIOS CONFIG
// ========================================================================

public class DcsBiosConfig
{
    public string Address { get; set; } =
        "0x7560";


    public ushort GetAddress()
    {
        string value =
            Address.Trim();


        if (value.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }


        if (!ushort.TryParse(
                value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out ushort address))
        {
            throw new FormatException(
                $"Ungültige DCS-BIOS-Adresse: {Address}");
        }


        return address;
    }
}


// ========================================================================
// BACKLIGHT CONFIG
// ========================================================================

public class BacklightConfig
{
    public List<BacklightLevel> Levels { get; set; } =
        new();
}


// ========================================================================
// BACKLIGHT LEVEL
// ========================================================================

public class BacklightLevel
{
    public int Level { get; set; }

    public int Min { get; set; }

    public int Max { get; set; }


    public bool Contains(
        ushort value)
    {
        return value >= Min &&
               value <= Max;
    }
}