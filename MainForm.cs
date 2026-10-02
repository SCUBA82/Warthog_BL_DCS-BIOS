using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WarthogLedControl;

public class MainForm : Form
{
    private DcsBiosListener? _dcsBios;
    private WarthogThrottle? _warthogThrottle;

    private readonly Label _valueLabel;
    private readonly Label _statusLabel;
    private readonly Label _backlightLabel;
    private readonly Label _addressLabel;

    private int _lastBacklightLevel = -1;

    private readonly AppConfig _config;
    private readonly string _configFile;

    private SetupTab _setupTab = null!;

    public MainForm()
    {
        // ============================================================
        // KONFIGURATION
        // ============================================================

        _configFile = Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.json");

        _config = AppConfig.Load(_configFile);

        // ============================================================
        // FORM
        // ============================================================

        Text = "Warthog LED Control";

        StartPosition = FormStartPosition.CenterScreen;

        ClientSize = new Size(620, 360);

        // ============================================================
        // TAB CONTROL
        // ============================================================

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill
        };

        var mainTab = new TabPage("Main");

        // SetupTab stellt seine eigene TabPage bereit.
        _setupTab = new SetupTab(
            _config,
            _configFile);

        _setupTab.ConfigurationChanged +=
            OnConfigurationChanged;

        tabControl.TabPages.Add(mainTab);
        tabControl.TabPages.Add(_setupTab.TabPage);

        Controls.Add(tabControl);

        // ============================================================
        // MAIN TAB AUFBAUEN
        // ============================================================

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
            Location = new Point(25, 25)
        };

        // ------------------------------------------------------------
        // DCS-BIOS Adresse
        // ------------------------------------------------------------

        _addressLabel = new Label
        {
            Text = GetAddressDisplayText(),
            AutoSize = true,
            Font = new Font(
                "Segoe UI",
                11),
            Location = new Point(25, 85)
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
            Location = new Point(430, 80)
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
            Location = new Point(25, 135)
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
            Location = new Point(25, 185)
        };

        // ------------------------------------------------------------
        // Controls zum Main-Tab hinzufügen
        // ------------------------------------------------------------

        mainTab.Controls.Add(titleLabel);
        mainTab.Controls.Add(_addressLabel);
        mainTab.Controls.Add(_valueLabel);
        mainTab.Controls.Add(_backlightLabel);
        mainTab.Controls.Add(_statusLabel);

        // ============================================================
        // DCS-BIOS STARTEN
        // ============================================================

        StartDcsBios();
    }


    // ====================================================================
    // ADDRESS DISPLAY
    // ====================================================================

    private string GetAddressDisplayText()
    {
        try
        {
            ushort address =
                _config.DcsBios.GetAddress();

            return $"DCS-BIOS Adresse: 0x{address:X4}";
        }
        catch
        {
            return $"DCS-BIOS Adresse: {_config.DcsBios.Address}";
        }
    }


    // ====================================================================
    // CONFIGURATION CHANGED
    // ====================================================================

    private void OnConfigurationChanged()
    {
        try
        {
            ushort address =
                _config.DcsBios.GetAddress();

            // --------------------------------------------------------
            // Debug-Adresse aktualisieren
            // --------------------------------------------------------

            if (_dcsBios != null)
            {
                _dcsBios.DebugAddress = address;
            }

            // --------------------------------------------------------
            // Letzten Backlight-Level zurücksetzen.
            //
            // Dadurch wird der neue Level beim nächsten DCS-BIOS
            // Wert garantiert an den Warthog übertragen.
            // --------------------------------------------------------

            _lastBacklightLevel = -1;

            // --------------------------------------------------------
            // Anzeige aktualisieren
            // --------------------------------------------------------

            if (InvokeRequired)
            {
                BeginInvoke(new Action(
                    UpdateConfigurationDisplay));

                return;
            }

            UpdateConfigurationDisplay();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Die neue Konfiguration konnte nicht übernommen werden:\n\n{ex.Message}",
                "Konfigurationsfehler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }


    private void UpdateConfigurationDisplay()
    {
        _addressLabel.Text =
            GetAddressDisplayText();
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

            // --------------------------------------------------------
            // DCS-BIOS Listener
            // --------------------------------------------------------

            _dcsBios =
                new DcsBiosListener();

            _dcsBios.DebugWrites = true;
            _dcsBios.DebugFrames = true;

            _dcsBios.DebugAddress =
                _config.DcsBios.GetAddress();

            // --------------------------------------------------------
            // Events
            // --------------------------------------------------------

            _dcsBios.LogMessage +=
                OnDcsBiosLog;

            _dcsBios.DcsBiosWrite +=
                OnDcsBiosWrite;

            _statusLabel.Text =
                "DCS-BIOS: wird gestartet...";

            // --------------------------------------------------------
            // Warthog Throttle
            // --------------------------------------------------------

            _warthogThrottle =
                new WarthogThrottle();

            _warthogThrottle.LogMessage +=
                OnWarthogLog;

            // --------------------------------------------------------
            // DCS-BIOS starten
            // --------------------------------------------------------

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
    // WARTHOG LOG
    // ====================================================================

    private void OnWarthogLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
                OnWarthogLog(message)));

            return;
        }

        _statusLabel.Text = message;
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

    private void OnDcsBiosWrite(
        ushort address,
        ushort value)
    {
        // ------------------------------------------------------------
        // Konfigurierte DCS-BIOS-Adresse holen
        // ------------------------------------------------------------

        ushort configuredAddress;

        try
        {
            configuredAddress =
                _config.DcsBios.GetAddress();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"FEHLER: Ungültige DCS-BIOS-Adresse: {ex.Message}");

            return;
        }

        // ------------------------------------------------------------
        // Nur die konfigurierte Adresse verarbeiten
        // ------------------------------------------------------------

        if (address != configuredAddress)
            return;

        // ------------------------------------------------------------
        // Passenden Backlight-Level suchen
        // ------------------------------------------------------------

        BacklightLevel? matchingLevel = null;

        foreach (var configuredLevel in
                 _config.Backlight.Levels)
        {
            if (configuredLevel.Contains(value))
            {
                matchingLevel = configuredLevel;
                break;
            }
        }

        // Kein Bereich für diesen Wert definiert.
        if (matchingLevel == null)
            return;

        int level =
            matchingLevel.Level;

        // ------------------------------------------------------------
        // Backlight nur aktualisieren, wenn sich der Level geändert hat
        // ------------------------------------------------------------

        if (level != _lastBacklightLevel)
        {
            _lastBacklightLevel = level;

            try
            {
                _warthogThrottle?
                    .SetBacklightLevel(level);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"FEHLER Warthog Backlight: {ex}");
            }
        }

        // ------------------------------------------------------------
        // UI aktualisieren
        // ------------------------------------------------------------

        UpdateBacklightDisplay(
            value,
            level);
    }


    // ====================================================================
    // BACKLIGHT UI
    // ====================================================================

    private void UpdateBacklightDisplay(ushort value, int level)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
                UpdateBacklightDisplay(
                    value,
                    level)));

            return;
        }

        _valueLabel.Text =
            $"{value}  (0x{value:X4})";

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
            if (_dcsBios != null)
            {
                _dcsBios.DcsBiosWrite -=
                    OnDcsBiosWrite;

                _dcsBios.LogMessage -=
                    OnDcsBiosLog;

                _dcsBios.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"MAIN: Fehler beim Stoppen von DCS-BIOS: {ex}");
        }

        base.OnFormClosed(e);
    }
}