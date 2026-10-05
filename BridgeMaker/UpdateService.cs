using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace RedroBridgeMaker;

static class UpdateService
{
    const string ReleaseApi = "https://api.github.com/repos/mhamedshasho/redro-bridge-maker/releases/tags/latest";
    static readonly HttpClient Client = CreateClient();

    public static async Task CheckAndOfferAsync(Window owner)
    {
        try
        {
            var release = await Client.GetFromJsonAsync<ReleaseInfo>(ReleaseApi);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName)) return;
            var current = CurrentBuild();
            var remoteVersion = await DownloadVersionAsync(release);
            if (string.IsNullOrWhiteSpace(remoteVersion) || string.Equals(current, remoteVersion.Trim(), StringComparison.OrdinalIgnoreCase)) return;

            var assetName = (Path.GetFileName(Environment.ProcessPath) ?? "").Contains("Portable", StringComparison.OrdinalIgnoreCase)
                ? "RedroBridgeMaker-Portable.exe" : "RedroBridgeMaker-Light.exe";
            var asset = release.Assets?.FirstOrDefault(a => string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));
            if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)) return;

            var answer = MessageBox.Show(owner,
                $"A new Redro Bridge Maker update is available.\n\nCurrent build: {Short(current)}\nNew build: {Short(remoteVersion)}\n\nUpdate and restart now?",
                "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;
            await DownloadAndRestartAsync(asset.BrowserDownloadUrl);
        }
        catch
        {
            // Updating is optional. Offline use and normal startup must never fail
            // because GitHub is unavailable or a release is temporarily missing.
        }
    }

    static async Task<string?> DownloadVersionAsync(ReleaseInfo release)
    {
        var asset = release.Assets?.FirstOrDefault(a => string.Equals(a.Name, "version.txt", StringComparison.OrdinalIgnoreCase));
        return asset == null ? null : await Client.GetStringAsync(asset.BrowserDownloadUrl);
    }

    static async Task DownloadAndRestartAsync(string url)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe)) return;
        var replacement = Path.Combine(Path.GetTempPath(), "RedroBridgeMaker-" + Guid.NewGuid().ToString("N") + ".exe");
        await using (var input = await Client.GetStreamAsync(url)) await using (var output = File.Create(replacement)) await input.CopyToAsync(output);

        var script = Path.Combine(Path.GetTempPath(), "RedroBridgeMaker-update-" + Guid.NewGuid().ToString("N") + ".cmd");
        var pid = Environment.ProcessId;
        File.WriteAllText(script, $"@echo off\r\n:wait\r\ntasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul\r\nif not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)\r\nmove /Y \"{replacement}\" \"{currentExe}\" >nul\r\nstart \"\" \"{currentExe}\"\r\ndel \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "/c", script } });
        Application.Current.Shutdown();
    }

    static string CurrentBuild() => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
    static string Short(string value) => value.Length > 12 ? value[..12] : value;
    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RedroBridgeMaker/1.0");
        return client;
    }

    sealed class ReleaseInfo
    {
        public string? TagName { get; set; }
        public List<AssetInfo>? Assets { get; set; }
    }
    sealed class AssetInfo
    {
        public string? Name { get; set; }
        public string? BrowserDownloadUrl { get; set; }
    }
}
