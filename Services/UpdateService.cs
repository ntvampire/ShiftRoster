using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
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
    public string? Sha256Checksum { get; set; }
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
        string sha256 = "";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            downloadUrl = root.TryGetProperty("windowsInstallerUrl", out var wProp) 
                ? wProp.GetString() ?? "" 
                : (root.TryGetProperty("downloadUrl", out var dProp) ? dProp.GetString() ?? "" : "");
            sha256 = root.TryGetProperty("windowsInstallerSha256", out var wsProp)
                ? wsProp.GetString() ?? ""
                : (root.TryGetProperty("sha256", out var sProp) ? sProp.GetString() ?? "" : "");
        }
        else
        {
            downloadUrl = root.TryGetProperty("linuxArchiveUrl", out var lProp) 
                ? lProp.GetString() ?? "" 
                : (root.TryGetProperty("downloadUrl", out var dProp) ? dProp.GetString() ?? "" : "");
            sha256 = root.TryGetProperty("linuxArchiveSha256", out var lsProp)
                ? lsProp.GetString() ?? ""
                : (root.TryGetProperty("sha256", out var sProp) ? sProp.GetString() ?? "" : "");
        }

        return EvaluateVersion(verStr, name, notes, downloadUrl, sha256);
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

    /// <summary>
    /// Проверка доверия к URL загрузки обновлений (строго HTTPS и белый список хостов GitHub).
    /// </summary>
    public static bool IsTrustedDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;

        string host = uri.Host.ToLowerInvariant();
        return host == "github.com"
            || host == "api.github.com"
            || host == "raw.githubusercontent.com"
            || host == "objects.githubusercontent.com"
            || host == "github-releases.githubusercontent.com"
            || host == "ntvampire.github.io"
            || host.EndsWith(".github.io", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Очистка и санитизация имени скачиваемого файла для предотвращения атак Path Traversal.
    /// </summary>
    public static string SanitizeFileName(string? rawName)
    {
        string defaultName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ShiftRoster-Setup.exe" : "ShiftRoster.tar.gz";
        if (string.IsNullOrWhiteSpace(rawName))
            return defaultName;

        // Извлекаем строго имя файла, отсекая любые пути
        string fileName = Path.GetFileName(rawName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName))
            return defaultName;

        char[] invalidChars = Path.GetInvalidFileNameChars();
        var cleanChars = fileName.Where(c => !invalidChars.Contains(c) && c != '/' && c != '\\').ToArray();
        string clean = new string(cleanChars).Trim();

        // Устраняем последовательности точек (Path Traversal) и ведущие точки
        while (clean.Contains(".."))
        {
            clean = clean.Replace("..", "");
        }
        clean = clean.TrimStart('.');

        if (string.IsNullOrWhiteSpace(clean))
            return defaultName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            clean += ".exe";
        }

        return clean;
    }

    public static UpdateInfo? EvaluateVersion(string rawVersion, string releaseName, string releaseNotes, string downloadUrl, string? sha256 = null)
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
            Sha256Checksum = string.IsNullOrWhiteSpace(sha256) ? null : sha256.Trim(),
            IsNewerThanCurrent = isNewer
        };
    }

    /// <summary>
    /// Загружает инсталлятор обновления в %TEMP% и запускает его, завершая текущий процесс.
    /// Перегрузка для совместимости с отчетом прогресса в процентах (0-100).
    /// </summary>
    public Task DownloadAndLaunchInstallerAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        return DownloadAndLaunchInstallerAsync(downloadUrl, progress, null, cancellationToken);
    }

    public Task DownloadAndLaunchInstallerAsync(string downloadUrl, IProgress<int>? progress, string? expectedSha256, CancellationToken cancellationToken = default)
    {
        IProgress<UpdateDownloadProgress>? adapter = progress == null ? null : new Progress<UpdateDownloadProgress>(p =>
        {
            if (p.Percent >= 0) progress.Report(p.Percent);
        });
        return DownloadAndLaunchInstallerAsync(downloadUrl, adapter, expectedSha256, cancellationToken);
    }

    /// <summary>
    /// Загружает инсталлятор обновления в %TEMP% и запускает его, завершая текущий процесс.
    /// Предоставляет расширенный прогресс загрузки (процент, прочитанные байты, общий размер).
    /// </summary>
    public Task DownloadAndLaunchInstallerAsync(string downloadUrl, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return DownloadAndLaunchInstallerAsync(downloadUrl, progress, null, cancellationToken);
    }

    /// <summary>
    /// Загружает инсталлятор обновления в %TEMP% с валидацией источника (whitelist HTTPS),
    /// проверкой контрольной суммы SHA-256 и безопасным запуском.
    /// </summary>
    public async Task DownloadAndLaunchInstallerAsync(
        string downloadUrl, 
        IProgress<UpdateDownloadProgress>? progress, 
        string? expectedSha256, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new ArgumentException("Не указан URL загрузки установщика.", nameof(downloadUrl));

        if (!IsTrustedDownloadUrl(downloadUrl))
            throw new SecurityException($"URL загрузки обновления «{downloadUrl}» не прошел проверку безопасности (разрешены только доверенные адреса GitHub по HTTPS).");

        string tempFolder = Path.Combine(Path.GetTempPath(), "ShiftRosterUpdate");
        if (!Directory.Exists(tempFolder))
        {
            Directory.CreateDirectory(tempFolder);
        }

        string rawFileName = "";
        try
        {
            rawFileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        }
        catch { }

        string fileName = SanitizeFileName(rawFileName);
        string targetPath = Path.Combine(tempFolder, fileName);

        try
        {
            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[64 * 1024];
                long totalRead = 0;
                int bytesRead;
                int lastPercent = -1;
                long lastReportedBytes = 0;

                // Начальный сигнал готовности
                progress?.Report(new UpdateDownloadProgress(0, 0, totalBytes));

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    int percent = (totalBytes.HasValue && totalBytes.Value > 0)
                        ? (int)((totalRead * 100) / totalBytes.Value)
                        : -1;

                    // Уведомляем UI при изменении процента или каждые 256 КБ
                    if (percent != lastPercent || (totalRead - lastReportedBytes) >= 256 * 1024)
                    {
                        lastPercent = percent;
                        lastReportedBytes = totalRead;
                        progress?.Report(new UpdateDownloadProgress(percent, totalRead, totalBytes));
                    }
                }

                progress?.Report(new UpdateDownloadProgress(100, totalRead, totalBytes ?? totalRead));
            }

            // Валидация размера: установочный пакет не может быть пустой заглушкой
            var fileInfo = new FileInfo(targetPath);
            if (fileInfo.Length < 100 * 1024) // минимум 100 КБ
            {
                throw new InvalidDataException($"Размер скачанного установочного пакета ({fileInfo.Length} байт) меньше допустимого минимума. Возможно, файл поврежден при передаче.");
            }

            // Валидация контрольной суммы SHA-256 (если передана в манифесте обновления)
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                using var sha256 = SHA256.Create();
                using var verifyStream = File.OpenRead(targetPath);
                byte[] hashBytes = await sha256.ComputeHashAsync(verifyStream, cancellationToken);
                string computedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                string cleanExpected = expectedSha256.Trim().ToLowerInvariant();

                if (!computedHash.Equals(cleanExpected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Контрольная сумма SHA-256 не совпадает!\nОжидалось: {cleanExpected}\nВычислено: {computedHash}");
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
                // Для других платформ (Linux/macOS) безопасно открываем папку со скачанным архивом
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFolder,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Очищаем частично загруженный файл при отмене или ошибке
            if (File.Exists(targetPath))
            {
                try { File.Delete(targetPath); } catch { }
            }
            throw;
        }
    }
}

/// <summary>
/// Информация о прогрессе загрузки установочного пакета обновления.
/// </summary>
public readonly record struct UpdateDownloadProgress(int Percent, long BytesRead, long? TotalBytes)
{
    public double BytesReadMegabytes => BytesRead / (1024.0 * 1024.0);
    public double? TotalMegabytes => TotalBytes.HasValue && TotalBytes.Value > 0 ? TotalBytes.Value / (1024.0 * 1024.0) : null;
}
