using System.Globalization;

namespace WarthogLedControl;

public class SetupTab
{
    private readonly AppConfig _config;
    private readonly string _configFile;

    private readonly TabPage _tabPage;

    private TextBox _addressTextBox = null!;
    private DataGridView _levelsGrid = null!;

    private Button _saveButton = null!;
    private Button _reloadButton = null!;

    public event Action? ConfigurationChanged;

    public TabPage TabPage => _tabPage;


    public SetupTab(AppConfig config, string configFile)
    {
        _config = config;
        _configFile = configFile;

        _tabPage = new TabPage("Setup");

        BuildUi();
        LoadFromConfig();
    }


    // ====================================================================
    // UI AUFBAU
    // ====================================================================

    private void BuildUi()
    {
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };

        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));

        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));


        // ================================================================
        // DCS-BIOS
        // ================================================================

        var dcsGroup = new GroupBox
        {
            Text = "DCS-BIOS",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var addressPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };

        addressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        addressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));


        var addressLabel = new Label
        {
            Text = "DCS-BIOS Backlight Adresse:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };


        _addressTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            MaxLength = 6
        };


        addressPanel.Controls.Add(addressLabel, 0, 0);

        addressPanel.Controls.Add(_addressTextBox, 1, 0);

        dcsGroup.Controls.Add(addressPanel);

        mainPanel.Controls.Add(dcsGroup, 0, 0);


        // ================================================================
        // BACKLIGHT LEVELS
        // ================================================================

        var backlightGroup = new GroupBox
        {
            Text = "Backlight Levels",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };


        _levelsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,

            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,

            AutoGenerateColumns = false,

            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,

            RowHeadersVisible = false,

            SelectionMode = DataGridViewSelectionMode.FullRowSelect,

            MultiSelect = false
        };


        _levelsGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Level",
                HeaderText = "Level",
                FillWeight = 20
            });


        _levelsGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Min",
                HeaderText = "Min",
                FillWeight = 40
            });


        _levelsGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Max",
                HeaderText = "Max",
                FillWeight = 40
            });


        backlightGroup.Controls.Add(_levelsGrid);

        mainPanel.Controls.Add(backlightGroup, 0, 1);


        // ================================================================
        // BUTTONS
        // ================================================================

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };


        _saveButton = new Button
        {
            Text = "Speichern",
            Width = 100,
            Height = 30
        };


        _reloadButton = new Button
        {
            Text = "Neu laden",
            Width = 100,
            Height = 30
        };


        _saveButton.Click += SaveButton_Click;

        _reloadButton.Click += ReloadButton_Click;


        buttonPanel.Controls.Add(_saveButton);

        buttonPanel.Controls.Add(_reloadButton);


        mainPanel.Controls.Add(buttonPanel, 0, 2);


        _tabPage.Controls.Add(mainPanel);
    }


    // ====================================================================
    // CONFIGURATION LADEN
    // ====================================================================

    private void LoadFromConfig()
    {
        _addressTextBox.Text =
            _config.DcsBios.Address;

        _levelsGrid.Rows.Clear();


        foreach (var level in
                 _config.Backlight.Levels)
        {
            _levelsGrid.Rows.Add(
                level.Level,
                level.Min,
                level.Max);
        }
    }


    // ====================================================================
    // RELOAD
    // ====================================================================

    private void ReloadButton_Click(
        object? sender,
        EventArgs e)
    {
        try
        {
            var newConfig = AppConfig.Load(_configFile);


            _config.DcsBios.Address = newConfig.DcsBios.Address;

            _config.Backlight.Levels = newConfig.Backlight.Levels;
            LoadFromConfig();

            MessageBox.Show(
                _tabPage.FindForm(),
                "Die Konfiguration wurde neu geladen.",
                "Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);


            ConfigurationChanged?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _tabPage.FindForm(),
                $"Fehler beim Laden der Konfiguration:\n\n{ex.Message}",
                "Fehler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }


    // ====================================================================
    // SPEICHERN
    // ====================================================================

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!ValidateConfiguration(out string error))
            {
                MessageBox.Show(
                    _tabPage.FindForm(),
                    error,
                    "Ungültige Konfiguration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }


            _config.DcsBios.Address = _addressTextBox.Text.Trim();


            var levels = new List<BacklightLevel>();


            foreach (DataGridViewRow row in _levelsGrid.Rows)
            {
                if (row.IsNewRow)
                    continue;


                int level = Convert.ToInt32(row.Cells["Level"].Value);


                int min = Convert.ToInt32(row.Cells["Min"].Value);


                int max = Convert.ToInt32(row.Cells["Max"].Value);


                levels.Add(
                    new BacklightLevel
                    {
                        Level = level,
                        Min = min,
                        Max = max
                    });
            }


            _config.Backlight.Levels = levels;


            _config.Save(_configFile);

            ConfigurationChanged?.Invoke();


            MessageBox.Show(
                _tabPage.FindForm(),
                "Die Konfiguration wurde gespeichert.",
                "Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _tabPage.FindForm(),
                $"Fehler beim Speichern:\n\n{ex.Message}",
                "Fehler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }


    // ====================================================================
    // VALIDIERUNG
    // ====================================================================

    private bool ValidateConfiguration(
        out string error)
    {
        error = string.Empty;


        // ------------------------------------------------------------
        // DCS-BIOS Adresse
        // ------------------------------------------------------------

        string address = _addressTextBox.Text.Trim();


        if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            address = address[2..];
        }


        if (!ushort.TryParse(
                address,
                NumberStyles.HexNumber,
                null,
                out _))
        {
            error = "Die DCS-BIOS-Adresse ist ungültig.";
            return false;
        }


        // ------------------------------------------------------------
        // Backlight Levels
        // ------------------------------------------------------------

        var levels = new List<BacklightLevel>();


        foreach (DataGridViewRow row in _levelsGrid.Rows)
        {
            if (row.IsNewRow)
                continue;

            if (!TryGetInt(row.Cells["Level"].Value, out int level))
            {
                error = "Ein Level enthält keinen gültigen Wert.";
                return false;
            }


            if (!TryGetInt(row.Cells["Min"].Value, out int min))
            {
                error = $"Level {level}: Min ist ungültig.";
                return false;
            }


            if (!TryGetInt(row.Cells["Max"].Value, out int max))
            {
                error = $"Level {level}: Max ist ungültig.";
                return false;
            }


            if (min < 0 || min > 65535)
            {
                error = $"Level {level}: Min muss zwischen 0 und 65535 liegen.";
                return false;
            }


            if (max < 0 || max > 65535)
            {
                error = $"Level {level}: Max muss zwischen 0 und 65535 liegen.";
                return false;
            }


            if (min > max)
            {
                error =
                    $"Level {level}: Min darf nicht größer als Max sein.";

                return false;
            }


            levels.Add(
                new BacklightLevel
                {
                    Level = level,
                    Min = min,
                    Max = max
                });
        }


        // ------------------------------------------------------------
        // Überlappungen prüfen
        // ------------------------------------------------------------

        for (int i = 0; i < levels.Count; i++)
        {
            for (int j = i + 1; j < levels.Count; j++)
            {
                if (levels[i].Min <= levels[j].Max && levels[j].Min <= levels[i].Max)
                {
                    error = $"Die Bereiche von Level {levels[i].Level} " +  $"und Level {levels[j].Level} überlappen.";
                    return false;
                }
            }
        }
        return true;
    }


    // ====================================================================
    // INTEGER PARSEN
    // ====================================================================

    private static bool TryGetInt(object? value, out int result)
    {
        result = 0;

        if (value == null)
          return false;

        return int.TryParse(value.ToString(), out result);
    }
}