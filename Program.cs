using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace WarthogLedControl;

internal static class Program
{
    // Eindeutiger Name für die Einzelinstanz-Prüfung.
    private const string MutexName =
        @"Global\WarthogLedControl_SingleInstance_5B1E7C0A";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(
            initiallyOwned: true,
            name: MutexName,
            createdNew: out bool createdNew);

        // Zweite Absicherung: läuft bereits ein Prozess mit gleichem
        // Namen? (erkennt auch ältere Versionen ohne Mutex)
        if (!createdNew || IsOtherProcessRunning())
        {
            MessageBox.Show(
                "WarthogLedControl läuft bereits.\n\n" +
                "Es kann nur eine Instanz gleichzeitig gestartet werden. " +
                "Die laufende Instanz befindet sich eventuell im Tray " +
                "(Infobereich der Taskleiste).",
                "Warthog LED Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return;
        }

        try
        {
            Application.Run(new MainForm());
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool IsOtherProcessRunning()
    {
        try
        {
            string? path = Environment.ProcessPath;

            if (string.IsNullOrEmpty(path))
                return false;

            string name = Path.GetFileNameWithoutExtension(path);

            // Start über "dotnet WarthogLedControl.dll": Prozessname
            // wäre "dotnet" -> würde fremde Prozesse treffen.
            if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                return false;

            int ownId = Environment.ProcessId;

            return Process.GetProcessesByName(name)
                .Any(p => p.Id != ownId);
        }
        catch
        {
            // Prüfung darf den Start nie verhindern, wenn sie selbst fehlschlägt.
            return false;
        }
    }
}