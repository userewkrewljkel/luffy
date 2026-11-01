using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LufsTaskbarMeter;

internal static class Program
{
    private const int HWND_BROADCAST = 0xffff;
    private const string Version = "v3.15 PREVIEW";
    // Each release gets its own lock; previous versions can never silently
    // swallow a launch of this executable, as happened with v3.7.
    private const string InstanceMutex = @"Local\LUFFY_SingleInstance_v3_15";
    internal static readonly uint WakeMessage = RegisterWindowMessage("LUFFY_WakeInstance_v3_15_2026");
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);

    [STAThread]
    private static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) => ReportError(e.Exception, "UI exception");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) LogError(ex, "Unhandled exception");
            };

            using var instance = new Mutex(true, InstanceMutex, out bool first);
            if (!first)
            {
                PostMessage(new IntPtr(HWND_BROADCAST), WakeMessage, IntPtr.Zero, IntPtr.Zero);
                MessageBox.Show("LUFFY v3.15 PREVIEW is already running. Its existing window has been requested to come to the front.",
                    "LUFFY v3.15 PREVIEW", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!CloseOlderVersions()) return;
            using var window = new MeterWindow();
            Application.Run(window);
        }
        catch (Exception ex)
        {
            ReportError(ex, "Startup failure");
        }
    }

    private static bool CloseOlderVersions()
    {
        // Never terminate another program without confirmation. Ask for a
        // graceful close so the old LUFFY can save its session/history.
        using var current = Process.GetCurrentProcess();
        Process[] older;
        try
        {
            older = Process.GetProcesses()
                .Where(p => p.Id != current.Id &&
                    p.ProcessName.StartsWith("LUFFY-v3.", StringComparison.OrdinalIgnoreCase) &&
                    p.SessionId == current.SessionId).ToArray();
        }
        catch (Exception ex)
        {
            LogError(ex, "Discover other LUFFY processes");
            return true; // A failed check must not prevent opening v3.15 PREVIEW.
        }
        try
        {
            if (older.Length == 0) return true;
            string versions = string.Join(", ", older.Select(p => p.ProcessName).Distinct());
            var answer = MessageBox.Show(
                $"An older LUFFY is still running ({versions}).\n\n" +
                "Close the old version gracefully and start LUFFY v3.15 PREVIEW?\n" +
                "This saves the previous measurement session before closing.",
                "LUFFY v3.15 PREVIEW – previous version detected",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return false;
            foreach (var process in older)
            {
                try
                {
                    if (!process.HasExited && !process.CloseMainWindow())
                    {
                        MessageBox.Show($"Could not close {process.ProcessName} automatically.\n" +
                            "Use its right-click menu → Exit, then launch v3.15 PREVIEW again.",
                            "LUFFY v3.15 PREVIEW", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    if (!process.WaitForExit(4000))
                    {
                        MessageBox.Show($"{process.ProcessName} is still closing.\n" +
                            "Close it with right-click → Exit and relaunch v3.15 PREVIEW.",
                            "LUFFY v3.15 PREVIEW", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    ReportError(ex, $"Closing {process.ProcessName}");
                    return false;
                }
            }
            return true;
        }
        finally { foreach (var old in older) old.Dispose(); }
    }

    internal static void LogError(Exception ex, string context)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            File.AppendAllText(Path.Combine(Settings.Folder, "errors.log"),
                $"{DateTimeOffset.Now:O} | LUFFY {Version} | {context}\n{ex}\n\n");
        }
        catch { /* error reporting must never crash the application */ }
    }

    internal static void ReportError(Exception ex, string context)
    {
        LogError(ex, context);
        try
        {
            MessageBox.Show($"{context}: {ex.Message}\n\n" +
                $"Details: {Path.Combine(Settings.Folder, "errors.log")}",
                "LUFFY v3.15 PREVIEW – error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
