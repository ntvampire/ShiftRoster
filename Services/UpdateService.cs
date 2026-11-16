using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShiftRoster.Services;

/// <summary>
/// Информация о доступном обновлении с GitHub
/// </summary>
public class UpdateInfo
{
    public string VersionTag { get; set; } = string.Empty;
    public Version? ParsedVersion { get; set; }
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public bool IsNewerThanCurrent { get; set; }
}

/// <summary>
/// Сервис автоматической проверки и загрузки обновлений ShiftRoster из GitHub Releases.
/// Проверяет статический манифест *.github.io и API GitHub Releases.
/// Работает бесшумно в фоновом потоке, не прерывая работу пользователя при отсутствии сети.
/// </summary>
public class UpdateService
{
    private static UpdateService? _instance;
    public static UpdateService Instance => _instance ??= new UpdateService();

    private readonly HttpClient _httpClient;

    // Первичный адрес (GitHub Pages *.github.io)
    public const string GitHubPagesUrl = "https://ntvampire.github.io/ShiftRoster/version.json";

    // Резервный адрес прямого API GitHub Releases
    public const string GitHubApiUrl = "https://api.github.com/repos/ntvampire/ShiftRoster/releases/latest";

    public UpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ShiftRoster-Updater");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>
    /// Текущая версия запущенного приложения
    /// </summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(1, 0, 3);
        }
    }

    public static string CurrentVersionString => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>
    /// Бесшумный поиск обновлений при старте приложения.
    /// Возвращает объект UpdateInfo, если доступна более новая версия, либо null.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Попытка получить версию со статической страницы *.github.io
        try
        {
            var updateFromPages = await CheckGitHubPagesAsync(cancellationToken);
            if (updateFromPages != null)
                return updateFromPages;
        }
        catch
        {
            // Переходим к запасному каналу
        }

        // 2. Попытка получить версию напрямую из GitHub Releases API
        try
        {
            var updateFromApi = await CheckGitHubApiAsync(cancellationToken);
            if (updateFromApi != null)
                return updateFromApi;
        }
        catch
        {
            // Ошибки сети, таймауты или отсутствие интернета игнорируются
        }

        return null;
    }

    private async Task<UpdateInfo?> CheckGitHubPagesAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(GitHubPagesUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string verStr = root.TryGetProperty("version", out var vProp) ? vProp.GetString() ?? "" : "";
        string name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
        string notes = root.TryGetProperty("body", out var bProp) ? bProp.GetString() ?? "" : "";
        
        string downloadUrl = "";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            downloadUrl = root.TryGetProperty("windowsInstallerUrl", out var wProp) 
                ? wProp.GetString() ?? "" 
                : (root.TryGetProperty("downloadUrl", out var dProp) ? dProp.GetString() ?? "" : "");
        }
        else
        {
            downloadUrl = root.TryGetProperty("linuxArchiveUrl", out var lProp) 
                ? lProp.GetString() ?? "" 
                : (root.TryGetProperty("downloadUrl", out var dProp) ? dProp.GetString() ?? "" : "");
        }

        return EvaluateVersion(verStr, name, notes, downloadUrl);
    }

    private async Task<UpdateInfo?> CheckGitHubApiAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(GitHubApiUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
        string name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : tag;
        string body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

        string downloadUrl = "";
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string assetName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                string assetUrl = asset.TryGetProperty("browser_download_url", out var au) ? au.GetString() ?? "" : "";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = assetUrl;
                    break;
                }
                else if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = assetUrl;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl) && !string.IsNullOrWhiteSpace(tag))
        {
            downloadUrl = $"https://github.com/ntvampire/ShiftRoster/releases/download/{tag}/ShiftRoster-Setup.exe";
        }

        return EvaluateVersion(tag, name, body, downloadUrl);
    }

    public static UpdateInfo? EvaluateVersion(string rawVersion, string releaseName, string releaseNotes, string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
            return null;

        string cleanVersion = rawVersion.TrimStart('v', 'V').Trim();
        if (!Version.TryParse(cleanVersion, out var parsedVersion))
        {
            // Поддержка двухкомпонентных версий вида 1.0 -> 1.0.0
            if (Version.TryParse($"{cleanVersion}.0", out var fallbackVer))
            {
                parsedVersion = fallbackVer;
            }
            else
            {
                return null;
            }
        }

        bool isNewer = parsedVersion > CurrentVersion;

        return new UpdateInfo
        {
            VersionTag = rawVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? rawVersion : $"v{cleanVersion}",
            ParsedVersion = parsedVersion,
            ReleaseName = string.IsNullOrWhiteSpace(releaseName) ? $"ShiftRoster v{cleanVersion}" : releaseName,
            ReleaseNotes = releaseNotes,
            DownloadUrl = downloadUrl,
            IsNewerThanCurrent = isNewer
        };
    }

    /// <summary>
    /// Загружает инсталлятор обновления в %TEMP% и запускает его, завершая текущий процесс.
    /// </summary>
    public async Task DownloadAndLaunchInstallerAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new ArgumentException("Не указан URL загрузки установщика.", nameof(downloadUrl));

        string tempFolder = Path.Combine(Path.GetTempPath(), "ShiftRosterUpdate");
        if (!Directory.Exists(tempFolder))
        {
            Directory.CreateDirectory(tempFolder);
        }

        string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "ShiftRoster-Setup.exe";
        string targetPath = Path.Combine(tempFolder, fileName);

        using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[64 * 1024];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    int percent = (int)((totalRead * 100) / totalBytes.Value);
                    progress?.Report(percent);
                }
            }
        }

        // Запуск установщика
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });

            // Завершаем работу программы для беспрепятственного обновления файлов
            Environment.Exit(0);
        }
        else
        {
            // Для других платформ открываем скачанный файл или папку
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });
        }
    }
}
