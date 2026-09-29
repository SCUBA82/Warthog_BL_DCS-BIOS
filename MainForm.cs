using System;
using System.Drawing;
using System.Windows.Forms;

namespace WarthogLedControl;

public class MainForm : Form
{
    private DcsBiosListener? _dcsBios;
    private WarthogThrottle? _warthogThrottle;
    private readonly Label _valueLabel;
    private readonly Label _statusLabel;
    private readonly Label _backlightLabel;
    private int _lastBacklightLevel = -1;
    private readonly AppConfig _config;

    public MainForm()
    {
        _config = AppConfig.Load("appsettings.json");
        Text = "Warthog LED Control";

        StartPosition = FormStartPosition.CenterScreen;

        ClientSize = new Size(520, 280);

        // ------------------------------------------------------------
        // Titel
        // ------------------------------------------------------------

        var titleLabel = new Label
        {
            Text = "DCS-BIOS Monitor",
            AutoSize = true,
            Font = new Font(
                "Segoe UI",
                16,
                FontStyle.Bold),
            Location = new Point(25, 20)
        };

        // ------------------------------------------------------------
        // DCS-BIOS Adresse
        // ------------------------------------------------------------

        var addressLabel = new Label
        {
            Text = "FA_18C_hornet_INSTR_INT_LT (0x7560):",
            AutoSize = true,
            Font = new Font(
                "Segoe UI",
                11),
            Location = new Point(25, 75)
        };

        // ------------------------------------------------------------
        // DCS-BIOS Wert
        // ------------------------------------------------------------

        _valueLabel = new Label
        {
            Text = "-",
            AutoSize = true,
            Font = new Font(
                "Consolas",
                18,
                FontStyle.Bold),
            Location = new Point(365, 70)
        };

        // ------------------------------------------------------------
        // Backlight-Level
        // ------------------------------------------------------------

        _backlightLabel = new Label
        {
            Text = "Backlight: -",
            AutoSize = true,
            Font = new Font(
                "Segoe UI",
                11,
                FontStyle.Bold),
            Location = new Point(25, 120)
        };

        // ------------------------------------------------------------
        // Status
        // ------------------------------------------------------------

        _statusLabel = new Label
        {
            Text = "DCS-BIOS: wird gestartet...",
            AutoSize = true,
            Font = new Font(
                "Segoe UI",
                10),
            Location = new Point(25, 165)
        };

        Controls.Add(titleLabel);
        Controls.Add(addressLabel);
        Controls.Add(_valueLabel);
        Controls.Add(_backlightLabel);
        Controls.Add(_statusLabel);

        StartDcsBios();
    }


    // ====================================================================
    // DCS-BIOS START
    // ====================================================================

    private void StartDcsBios()
    {
        try
        {
            _statusLabel.Text =
                "DCS-BIOS: Listener wird erstellt...";

            _dcsBios = new DcsBiosListener();

            _dcsBios.DebugWrites = true;
            _dcsBios.DebugFrames = true;

            // FA_18C_hornet_INSTR_INT_LT
            _dcsBios.DebugAddress = _config.DcsBios.GetAddress();

            _dcsBios.LogMessage += OnDcsBiosLog;

            _dcsBios.DcsBiosWrite += OnDcsBiosWrite;

            _statusLabel.Text =
                "DCS-BIOS: wird gestartet...";

            _warthogThrottle = new WarthogThrottle();

            _warthogThrottle.LogMessage += message =>
            {
                _statusLabel.Text = message;
            };
            _dcsBios.Start();

            _statusLabel.Text =
                "DCS-BIOS: gestartet – warte auf Daten";
        }
        catch (Exception ex)
        {
            _statusLabel.Text =
                "DCS-BIOS: FEHLER";

            MessageBox.Show(
                ex.ToString(),
                "DCS-BIOS Startfehler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }


    // ====================================================================
    // DCS-BIOS LOG
    // ====================================================================

    private void OnDcsBiosLog(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
                OnDcsBiosLog(message)));

            return;
        }

        if (message.Contains(
                "ERROR",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains(
                "FEHLER",
                StringComparison.OrdinalIgnoreCase))
        {
            _statusLabel.Text = message;
        }
    }


    // ====================================================================
    // DCS-BIOS WRITE
    // ====================================================================

private void OnDcsBiosWrite(ushort address, ushort value)
{
       if (address != _config.DcsBios.GetAddress())
        return;
    
    BacklightLevel? matchingLevel = null;
    
    foreach (var configuredLevel in _config.Backlight.Levels)
    {
        if (configuredLevel.Contains(value))
        {
            matchingLevel = configuredLevel;
            break;
        }
    }
    
    if (matchingLevel == null)
        return;
    
    int level = matchingLevel.Level;

    // Nur aktualisieren, wenn sich der Backlight-Level geändert hat.
    if (level != _lastBacklightLevel)
    {
        _lastBacklightLevel = level;

        try
        {
            _warthogThrottle?.SetBacklightLevel(level);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"FEHLER Warthog Backlight: {ex}");
        }
    }

    if (InvokeRequired)
    {
        BeginInvoke(() =>
        {
            _valueLabel.Text = value.ToString();
            _backlightLabel.Text = $"Backlight: {level}";
        });
    }
    else
    {
        _valueLabel.Text = value.ToString();
        _backlightLabel.Text = $"Backlight: {level}";
    }
}

    // ====================================================================
    // BACKLIGHT UI
    // ====================================================================

    private void UpdateBacklightDisplay(ushort value, int level)
    {
        _valueLabel.Text =  $"{value}  (0x{value:X4})";

        _backlightLabel.Text = $"Backlight: Level {level}";

        _statusLabel.Text =
            "DCS-BIOS: Daten werden empfangen";
    }



    // ====================================================================
    // FORM CLOSED
    // ====================================================================

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            _dcsBios?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"MAIN: Fehler beim Stoppen von DCS-BIOS: {ex}");
        }

        base.OnFormClosed(e);
    }
}
