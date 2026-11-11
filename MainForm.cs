using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WarthogLedControl;

public class MainForm : Form
{
    // Nach dieser Zeit ohne DCS-BIOS-Paket wird "keine Pakete" angezeigt.
    private static readonly TimeSpan DcsTimeout = TimeSpan.FromSeconds(5);

    private static readonly Color ColorOk = Color.FromArgb(0, 140, 0);
    private static readonly Color ColorWait = Color.DarkOrange;
    private static readonly Color ColorError = Color.Firebrick;

    private DcsBiosListener? _dcsBios;
    private WarthogThrottle? _warthogThrottle;

    private readonly Label _valueLabel;
    private readonly Label _dcsStatusLabel;
    private readonly Label _dcsDetailLabel;
    private readonly Label _warthogLabel;
    private readonly Label _backlightLabel;
    private readonly Label _addressLabel;

    private int _lastBacklightLevel = -1;

    private readonly AppConfig _config;
    private readonly string _configFile;

    private SetupTab _setupTab = null!;

    // ============================================================
    // DCS-BIOS STATUS
    // ============================================================

    // Zyklische Prüfung, ob noch Pakete ankommen
    private readonly System.Windows.Forms.Timer _statusTimer;

    // Fehlertext, falls der Listener nicht gestartet werden konnte
    private string? _dcsStartError;

    // Konfigurierte Adresse (gecacht, damit nicht bei jedem Write geparst wird)
    private volatile int _configuredAddress = -1;

    // true = die konfigurierte Adresse wurde seit Start/Änderung empfangen
    private volatile bool _addressSeen;

    // ============================================================
    // MANUELLER TEST
    // ============================================================

    private CheckBox _manualCheckBox = null!;
    private readonly List<Button> _levelButtons = new();

    // true = DCS-BIOS steuert das Backlight NICHT
    private volatile bool _manualMode;

    // ============================================================
    // TRAY
    // ============================================================

    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;

    // true = Anwendung darf tatsächlich beendet werden
    private bool _allowExit;

    // true = Fenster beim Programmstart nicht anzeigen (nur Tray)
    private bool _startHidden;

    public MainForm()
    {
        // ============================================================
        // KONFIGURATION
        // ============================================================

        _configFile = Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.json");

        _config = AppConfig.Load(_configFile);

        _startHidden = _config.StartMinimized;

        RefreshConfiguredAddress();

        // ============================================================
        // FORM
        // ============================================================

        Text = "Warthog LED Control";

        StartPosition = FormStartPosition.CenterScreen;

        ClientSize = new Size(620, 450);

        Icon = LoadApplicationIcon();

        ShowIcon = true;

        // ============================================================
        // TRAY ICON
        // ============================================================

        _trayMenu = new ContextMenuStrip();

        _trayMenu.Items.Add("Öffnen", null, TrayOpen_Click);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Beenden", null, TrayExit_Click);

        _trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "Warthog LED Control",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };

        _trayIcon.DoubleClick += TrayIcon_DoubleClick;

        Resize += MainForm_Resize;

        // ============================================================
        // TAB CONTROL
        // ============================================================

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill
        };

        var mainTab = new TabPage("Main");

        _setupTab = new SetupTab(_config, _configFile);
        _setupTab.ConfigurationChanged += OnConfigurationChanged;

        tabControl.TabPages.Add(mainTab);
        tabControl.TabPages.Add(_setupTab.TabPage);

        Controls.Add(tabControl);

        // ============================================================
        // MAIN TAB AUFBAUEN
        // ============================================================

        var titleLabel = new Label
        {
            Text = "DCS-BIOS Monitor",
            AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Location = new Point(25, 25)
        };

        _addressLabel = new Label
        {
            Text = GetAddressDisplayText(),
            AutoSize = true,
            Font = new Font("Segoe UI", 11),
            Location = new Point(25, 85)
        };

        _valueLabel = new Label
        {
            Text = "-",
            AutoSize = true,
            Font = new Font("Consolas", 18, FontStyle.Bold),
            Location = new Point(430, 80)
        };

        _backlightLabel = new Label
        {
            Text = "Backlight: -",
            AutoSize = true,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Location = new Point(25, 130)
        };

        // DCS-BIOS Verbindungsstatus
        _dcsStatusLabel = new Label
        {
            Text = "DCS-BIOS: wird gestartet...",
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = ColorWait,
            Location = new Point(25, 175)
        };

        // Zähler / letzte Aktivität
        _dcsDetailLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = SystemColors.GrayText,
            Location = new Point(25, 200)
        };

        // Warthog-Status (getrennt vom DCS-BIOS-Status)
        _warthogLabel = new Label
        {
            Text = "Warthog: -",
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            Location = new Point(25, 225)
        };

        var testGroup = new GroupBox
        {
            Text = "Manueller Test",
            Location = new Point(25, 260),
            Size = new Size(560, 100)
        };

        _manualCheckBox = new CheckBox
        {
            Text = "Manuell steuern (DCS-BIOS ignorieren)",
            AutoSize = true,
            Location = new Point(15, 25)
        };

        _manualCheckBox.CheckedChanged += ManualCheckBox_CheckedChanged;

        testGroup.Controls.Add(_manualCheckBox);

        for (int level = 0; level <= 3; level++)
        {
            int lvl = level;

            var button = new Button
            {
                Text = $"Level {lvl}",
                Size = new Size(110, 32),
                Location = new Point(15 + lvl * 125, 55),
                Enabled = false
            };

            button.Click += (_, _) => SetManualLevel(lvl);

            _levelButtons.Add(button);
            testGroup.Controls.Add(button);
        }

        var exitButton = new Button
        {
            Text = "Beenden",
            AutoSize = true,
            Location = new Point(25, 375)
        };

        exitButton.Click += (_, _) =>
        {
            _allowExit = true;
            _trayIcon.Visible = false;
            Close();
        };

        mainTab.Controls.Add(titleLabel);
        mainTab.Controls.Add(_addressLabel);
        mainTab.Controls.Add(_valueLabel);
        mainTab.Controls.Add(_backlightLabel);
        mainTab.Controls.Add(_dcsStatusLabel);
        mainTab.Controls.Add(_dcsDetailLabel);
        mainTab.Controls.Add(_warthogLabel);
        mainTab.Controls.Add(testGroup);
        mainTab.Controls.Add(exitButton);

        // ============================================================
        // STATUS-TIMER
        // ============================================================

        _statusTimer = new System.Windows.Forms.Timer
        {
            Interval = 500
        };

        _statusTimer.Tick += (_, _) => UpdateDcsStatus();

        // ============================================================
        // DCS-BIOS STARTEN
        // ============================================================

        StartDcsBios();

        _statusTimer.Start();

        UpdateDcsStatus();
    }


    // ====================================================================
    // START MINIMIERT (nur Tray)
    // ====================================================================

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden)
        {
            // Application.Run() versucht das Fenster anzuzeigen.
            // Das unterdrücken wir, das Handle wird aber trotzdem
            // erzeugt, damit BeginInvoke() funktioniert.
            if (!IsHandleCreated)
                CreateHandle();

            value = false;
        }

        base.SetVisibleCore(value);
    }


    // ====================================================================
    // APPLICATION ICON
    // ====================================================================

    private static Icon LoadApplicationIcon()
    {
        try
        {
            string iconPath = Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                "Icon.ico");

            if (File.Exists(iconPath))
                return new Icon(iconPath);

            System.Diagnostics.Debug.WriteLine(
                $"MAIN: Icon-Datei nicht gefunden: {iconPath}");

            // Fallback: das in die EXE eingebettete Icon
            // (<ApplicationIcon> im csproj).
            string? exePath = Environment.ProcessPath;

            if (!string.IsNullOrEmpty(exePath) &&
                !Path.GetFileNameWithoutExtension(exePath)
                    .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                Icon? exeIcon = Icon.ExtractAssociatedIcon(exePath);

                if (exeIcon != null)
                    return exeIcon;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"MAIN: Icon konnte nicht geladen werden: {ex}");
        }

        return SystemIcons.Application;
    }


    // ====================================================================
    // TRAY
    // ====================================================================

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
            Hide();
    }

    private void TrayIcon_DoubleClick(object? sender, EventArgs e)
        => ShowFromTray();

    private void TrayOpen_Click(object? sender, EventArgs e)
        => ShowFromTray();

    private void ShowFromTray()
    {
        // Ab jetzt darf das Fenster normal angezeigt werden.
        _startHidden = false;

        Show();

        WindowState = FormWindowState.Normal;

        Activate();
        BringToFront();
    }

    private void TrayExit_Click(object? sender, EventArgs e)
    {
        _allowExit = true;
        _trayIcon.Visible = false;
        Close();
    }


    // ====================================================================
    // ADRESSE
    // ====================================================================

    private string GetAddressDisplayText()
    {
        try
        {
            ushort address = _config.DcsBios.GetAddress();
            return $"DCS-BIOS Adresse: 0x{address:X4}";
        }
        catch
        {
            return $"DCS-BIOS Adresse: {_config.DcsBios.Address}";
        }
    }

    // Aktualisiert die gecachte Adresse. -1 = ungültig.
    private void RefreshConfiguredAddress()
    {
        try
        {
            _configuredAddress = _config.DcsBios.GetAddress();
        }
        catch
        {
            _configuredAddress = -1;
        }

        _addressSeen = false;
    }


    // ====================================================================
    // CONFIGURATION CHANGED
    // ====================================================================

    private void OnConfigurationChanged()
    {
        try
        {
            RefreshConfiguredAddress();

            if (_dcsBios != null && _configuredAddress >= 0)
                _dcsBios.DebugAddress = (ushort)_configuredAddress;

            // Nächster DCS-BIOS-Wert wird garantiert an den Warthog gesendet.
            _lastBacklightLevel = -1;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateConfigurationDisplay));
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
        _addressLabel.Text = GetAddressDisplayText();
        UpdateDcsStatus();
    }


    // ====================================================================
    // DCS-BIOS START
    // ====================================================================

    private void StartDcsBios()
    {
        try
        {
            _dcsBios = new DcsBiosListener
            {
                DebugWrites = true,

                // Frame-Meldungen kommen ~30x pro Sekunde und würden
                // nur unnötig Last erzeugen.
                DebugFrames = false
            };

            if (_configuredAddress >= 0)
                _dcsBios.DebugAddress = (ushort)_configuredAddress;

            _dcsBios.LogMessage += OnDcsBiosLog;
            _dcsBios.DcsBiosWrite += OnDcsBiosWrite;

            _warthogThrottle = new WarthogThrottle();
            _warthogThrottle.LogMessage += OnWarthogLog;

            _dcsBios.Start();

            _dcsStartError = null;
        }
        catch (Exception ex)
        {
            _dcsStartError = ex.Message;

            MessageBox.Show(
                ex.ToString(),
                "DCS-BIOS Startfehler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }


    // ====================================================================
    // DCS-BIOS STATUSANZEIGE
    // ====================================================================

    private void UpdateDcsStatus()
    {
        // ------------------------------------------------------------
        // Listener konnte nicht gestartet werden
        // ------------------------------------------------------------

        if (_dcsBios == null || !_dcsBios.IsRunning)
        {
            SetDcsStatus(
                _dcsStartError != null
                    ? $"DCS-BIOS: Listener-Fehler – {_dcsStartError}"
                    : "DCS-BIOS: Listener nicht aktiv",
                ColorError);

            _dcsDetailLabel.Text = "";
            return;
        }

        TimeSpan? since = _dcsBios.TimeSinceLastPacket;

        // ------------------------------------------------------------
        // Noch kein einziges Paket empfangen
        // ------------------------------------------------------------

        if (since == null)
        {
            SetDcsStatus(
                "DCS-BIOS: Warte auf Pakete (DCS gestartet? Mission läuft?)",
                ColorWait);

            _dcsDetailLabel.Text =
                $"Lausche auf {DcsBiosListener.MulticastAddress}:{DcsBiosListener.Port}";

            _valueLabel.ForeColor = SystemColors.GrayText;
            return;
        }

        string detail =
            $"Pakete: {_dcsBios.ReceivedUdpPackets:N0}   " +
            $"Frames: {_dcsBios.ReceivedFrames:N0}   " +
            $"Letztes Paket: vor {FormatAge(since.Value)}";

        // ------------------------------------------------------------
        // Pakete bleiben aus (> 5 s)
        // ------------------------------------------------------------

        if (since.Value > DcsTimeout)
        {
            SetDcsStatus(
                $"DCS-BIOS: KEINE PAKETE seit {FormatAge(since.Value)}",
                ColorError);

            _dcsDetailLabel.Text = detail;

            // Der letzte Wert ist veraltet.
            _valueLabel.ForeColor = SystemColors.GrayText;
            return;
        }

        // ------------------------------------------------------------
        // Pakete kommen an
        // ------------------------------------------------------------

        if (_addressSeen)
        {
            SetDcsStatus("DCS-BIOS: Pakete werden empfangen", ColorOk);
        }
        else
        {
            SetDcsStatus(
                "DCS-BIOS: Pakete werden empfangen – " +
                "konfigurierte Adresse noch nicht gesehen",
                ColorWait);
        }

        _dcsDetailLabel.Text = detail;

        _valueLabel.ForeColor = SystemColors.ControlText;
    }

    private void SetDcsStatus(string text, Color color)
    {
        if (_dcsStatusLabel.Text != text)
            _dcsStatusLabel.Text = text;

        if (_dcsStatusLabel.ForeColor != color)
            _dcsStatusLabel.ForeColor = color;
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 10)
            return $"{age.TotalSeconds:0.0} s";

        if (age.TotalSeconds < 60)
            return $"{(int)age.TotalSeconds} s";

        return $"{(int)age.TotalMinutes} min {age.Seconds} s";
    }


    // ====================================================================
    // MANUELLER TEST
    // ====================================================================

    private void ManualCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        _manualMode = _manualCheckBox.Checked;

        foreach (var button in _levelButtons)
            button.Enabled = _manualMode;

        if (!_manualMode)
        {
            // Zurück zu DCS-BIOS: nächster Wert wird garantiert gesendet.
            _lastBacklightLevel = -1;
            _backlightLabel.Text = "Backlight: -";
        }
        else
        {
            _backlightLabel.Text = "Backlight: manuell";
        }
    }

    private void SetManualLevel(int level)
    {
        try
        {
            if (_warthogThrottle == null)
            {
                _warthogLabel.Text =
                    "Warthog: FEHLER – Objekt nicht initialisiert";
                return;
            }

            _warthogThrottle.SetBacklightLevel(level);

            _backlightLabel.Text =
                $"Backlight: Level {level} (manuell)";
        }
        catch (Exception ex)
        {
            _warthogLabel.Text = $"Warthog: FEHLER – {ex.Message}";
        }
    }


    // ====================================================================
    // LOGS
    // ====================================================================

    private void OnWarthogLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnWarthogLog(message)));
            return;
        }

        // Nur das Warthog-Label – der DCS-BIOS-Status bleibt unberührt.
        _warthogLabel.Text = $"Warthog: {message}";
    }

    private void OnDcsBiosLog(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
    }


    // ====================================================================
    // DCS-BIOS WRITE
    // ====================================================================

    private void OnDcsBiosWrite(ushort address, ushort value)
    {
        int configuredAddress = _configuredAddress;

        if (configuredAddress < 0 || address != configuredAddress)
            return;

        _addressSeen = true;

        // Passenden Backlight-Level suchen
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

        if (!_manualMode && level != _lastBacklightLevel)
        {
            _lastBacklightLevel = level;

            try
            {
                _warthogThrottle?.SetBacklightLevel(level);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"FEHLER Warthog Backlight: {ex}");
            }
        }

        UpdateBacklightDisplay(value, level);
    }

    private void UpdateBacklightDisplay(ushort value, int level)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
                UpdateBacklightDisplay(value, level)));
            return;
        }

        _valueLabel.Text = $"{value}  (0x{value:X4})";

        if (_manualMode)
            return;

        _backlightLabel.Text = $"Backlight: Level {level}";
    }


    // ====================================================================
    // FORM CLOSING
    // ====================================================================

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Klick auf X: nicht beenden, sondern in den Tray.
        if (!_allowExit &&
            e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            _statusTimer.Stop();
            _statusTimer.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"MAIN: Fehler beim Stoppen des Timers: {ex}");
        }

        try
        {
            _trayIcon.Visible = false;
            _trayIcon.DoubleClick -= TrayIcon_DoubleClick;
            _trayMenu.Dispose();
            _trayIcon.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"MAIN: Fehler beim Entfernen des Tray-Icons: {ex}");
        }

        try
        {
            if (_dcsBios != null)
            {
                _dcsBios.DcsBiosWrite -= OnDcsBiosWrite;
                _dcsBios.LogMessage -= OnDcsBiosLog;
                _dcsBios.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"MAIN: Fehler beim Stoppen von DCS-BIOS: {ex}");
        }

        try
        {
            if (_warthogThrottle != null)
            {
                _warthogThrottle.LogMessage -= OnWarthogLog;
                _warthogThrottle.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"MAIN: Fehler beim Freigeben des Warthog: {ex}");
        }

        base.OnFormClosed(e);
    }
}