using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace DS5Link;

internal static class ReleaseUpdate
{
    const string Repository = "Q1W1RTY/DS5Link";
    const string Installer = "DS5Link-Setup.exe";
    const int MaxInstallerBytes = 250 * 1024 * 1024;

    internal static Version? ReadVersion(JsonElement release, Version installed)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;
        var tag = release.GetProperty("tag_name").GetString();
        return tag != null && tag.StartsWith('v') &&
            Version.TryParse(tag[1..], out var version) && version.Build >= 0 && version.Revision < 0 &&
            tag == $"v{version}" && version > installed ? version : null;
    }

    internal static byte[] ReadChecksum(string manifest)
    {
        var fields = manifest.TrimStart('\uFEFF').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 2 || fields[0].Length != 64 || fields[1] != Installer)
            throw new IOException("Invalid update checksum file.");
        return Convert.FromHexString(fields[0]);
    }

    internal static async Task<string?> DownloadAsync(Version installed, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var version = await CheckAsync(client, installed, cancellationToken);
        if (version == null) return null;

        var url = $"https://github.com/{Repository}/releases/download/v{version}/";
        using var manifest = await client.GetAsync(url + "SHA256.txt", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        manifest.EnsureSuccessStatusCode();
        var manifestBytes = new byte[256];
        await using var manifestStream = await manifest.Content.ReadAsStreamAsync(cancellationToken);
        var count = await manifestStream.ReadAtLeastAsync(manifestBytes, manifestBytes.Length, false, cancellationToken);
        if (count == manifestBytes.Length) throw new IOException("Invalid update checksum file.");
        var expected = ReadChecksum(System.Text.Encoding.UTF8.GetString(manifestBytes, 0, count));

        var folder = Path.Combine(Settings.DirectoryPath, "Update");
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, Installer);
        var temporary = Path.Combine(folder, Guid.NewGuid() + ".part");
        try
        {
            using var response = await client.GetAsync(url + Installer, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxInstallerBytes)
                throw new IOException("Update is too large.");
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                var buffer = new byte[128 * 1024];
                long size = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    size += read;
                    if (size > MaxInstallerBytes) throw new IOException("Update is too large.");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                file.Position = 0;
                if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(file, cancellationToken), expected))
                    throw new IOException("Update SHA-256 mismatch.");
            }
            File.Move(temporary, destination, true);
            return destination;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static async Task<Version?> CheckAsync(HttpClient client, Version installed, CancellationToken cancellationToken)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DS5Link-Updater");
        var release = await client.GetFromJsonAsync<JsonElement>(
            $"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken);
        return ReadVersion(release, installed);
    }
}

internal sealed partial class MainWindow
{
    readonly DispatcherTimer updateCheckTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    static Version InstalledVersion => Version.Parse(typeof(MainWindow).Assembly.GetName().Version!.ToString(3));

    void StartUpdateChecks()
    {
        updateCheckTimer.Tick += CheckForUpdate;
        Closed += (_, _) => updateCheckTimer.Stop();
        updateCheckTimer.Start();
    }

    async void CheckForUpdate(object? sender, EventArgs e)
    {
        updateCheckTimer.Stop();
        try
        {
            if (!UpdateButton.IsEnabled || !IsInstalledCopy()) return;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = new HttpClient();
            var version = await ReleaseUpdate.CheckAsync(client, InstalledVersion, deadline.Token);
            if (exiting || exitRequested) return;
            var label = version == null ? L.T("Обновить DS5Link") : L.F("Обновить до {0}", version);
            ToolTip.SetTip(UpdateButton, label);
            AutomationProperties.SetName(UpdateButton, label);
            if (version == null) UpdateButton.ClearValue(Button.ForegroundProperty);
            else UpdateButton.Foreground = BlueBrush;
            if (version == null) return;

            var settings = Settings.Load();
            if (Version.TryParse(settings.LastNotifiedUpdate, out var previous) && previous >= version) return;
            Notify("DS5Link", L.F("Доступна версия {0}. Нажмите «Обновить» в приложении.", version));
            settings.LastNotifiedUpdate = version.ToString();
            settings.Save();
        }
        catch (Exception error)
        {
            Trace.WriteLine(error);
        }
        finally
        {
            if (!exiting)
            {
                updateCheckTimer.Interval = TimeSpan.FromHours(4);
                updateCheckTimer.Start();
            }
        }
    }

    static bool IsInstalledCopy()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DS5Link");
        var location = key?.GetValue("InstallLocation") as string;
        return location != null && string.Equals(Path.TrimEndingDirectorySeparator(location),
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
    }

    async void UpdateApplication(object? sender, RoutedEventArgs e)
    {
        if (preview || commanding || exitRequested || !UpdateButton.IsEnabled) return;
        UpdateButton.IsEnabled = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            if (!IsInstalledCopy())
            {
                ShowUpdateFeedback(L.T("Обновление доступно из установленной версии DS5Link."));
                return;
            }
            ShowUpdateFeedback(L.T("Проверяем и скачиваем обновление…"));
            var installer = await ReleaseUpdate.DownloadAsync(InstalledVersion, deadline.Token);
            if (installer == null)
            {
                ShowUpdateFeedback(L.T("Установлена последняя версия."));
                return;
            }
            if (exiting || exitRequested || commanding)
            {
                ShowUpdateFeedback(L.T("Дождитесь завершения текущей операции."));
                return;
            }
            ShowUpdateFeedback(L.T("Подтвердите обновление в окне Windows."));
            using var process = Process.Start(new ProcessStartInfo(installer, "--update-installed")
            {
                UseShellExecute = true
            }) ?? throw new IOException("Could not start updater.");
            await process.WaitForExitAsync();
            if (!exiting)
                ShowUpdateFeedback(L.T(process.ExitCode == 1223 ? "Обновление отменено." : "Обновление не применилось."));
        }
        catch (Exception error)
        {
            Trace.WriteLine(error);
            ShowUpdateFeedback(L.T("Не удалось скачать или проверить обновление."));
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    void ShowUpdateFeedback(string message)
    {
        UpdateFeedback.Text = message;
        UpdateFeedback.IsVisible = true;
    }
}
