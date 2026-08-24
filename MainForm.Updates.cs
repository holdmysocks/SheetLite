using System.Diagnostics;
using System.Text.Json;

namespace SheetLite;

internal sealed partial class MainForm
{
    private bool updateCheckRunning;

    private async Task CheckForUpdatesAsync(bool showCurrent)
    {
        if (updateCheckRunning) return;
        updateCheckRunning = true;

        try
        {
            AppUpdate? update = await UpdateChecker.CheckAsync();
            if (IsDisposed) return;

            if (update is null)
            {
                if (showCurrent) ShowNotice("Software update", $"SheetLite {AppVersion} is up to date.");
                return;
            }

            DialogResult choice = MessageBox.Show(
                this,
                $"SheetLite {update.Version.ToString(3)} is available. You have {AppVersion}.\n\nOpen the GitHub release page to download it?",
                "SheetLite update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (choice == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(update.ReleasePage.AbsoluteUri) { UseShellExecute = true });
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (showCurrent && !IsDisposed)
                ShowNotice("Software update", "SheetLite could not check GitHub for updates. Check your internet connection and try again.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (showCurrent && !IsDisposed)
                ShowNotice("Software update", "The GitHub release page could not be opened in your browser.");
        }
        finally
        {
            updateCheckRunning = false;
        }
    }
}
