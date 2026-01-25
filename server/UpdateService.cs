using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Glance.Server;

internal sealed class UpdateService
{
    private const string ManifestFileName = "glance.update.json";
    private const string ManifestFormat = "glance-update-1";
    private const string HashAlgorithmName = "sha256";
    private static readonly string[] ForbiddenTopLevelFolders = ["data", "blobs"];
    private static readonly string VersionFormat = "yyyy-MM-dd HH:mm";

    private readonly AppPaths _paths;
    private readonly ILogger<UpdateService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateService(AppPaths paths, ILogger<UpdateService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<string> ApplyUpdateAsync(IFormFile package, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            return await ApplyUpdateInternalAsync(package, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> ApplyUpdateInternalAsync(IFormFile package, CancellationToken token)
    {
        try
        {
            var updatesRoot = Path.Combine(_paths.DataDirectory, "updates");
            Directory.CreateDirectory(updatesRoot);

            var updateId = Guid.NewGuid().ToString("N");
            var zipPath = Path.Combine(updatesRoot, $"update-{updateId}.zip");
            _logger.LogInformation("Staging update package {FileName} ({Size} bytes) to {Path}.", package.FileName, package.Length, zipPath);
            AppendDesktopLog($"Update: staging {package.FileName} ({package.Length} bytes).");
            await using (var output = File.Create(zipPath))
            {
                await package.CopyToAsync(output, token);
            }

            using var archive = ZipFile.OpenRead(zipPath);
            var manifestEntry = FindManifestEntry(archive);
            var manifest = await ReadManifestAsync(manifestEntry, token);
            ValidateManifest(manifest);
            _logger.LogInformation(
                "Update manifest: version {Version}, algorithm {Algorithm}, format {Format}.",
                manifest.Version,
                manifest.Algorithm,
                manifest.Format);
            AppendDesktopLog($"Update: manifest version {manifest.Version}, algorithm {manifest.Algorithm}.");

            var currentVersion = ParseVersion(BuildInfo.Version, "current");
            var updateVersion = ParseVersion(manifest.Version, "update");
            if (updateVersion <= currentVersion)
            {
                throw new UpdatePackageException(
                    $"Update version {manifest.Version} is not newer than current version {BuildInfo.Version}.");
            }

            var stagingRoot = Path.Combine(updatesRoot, $"staging-{updateId}");
            Directory.CreateDirectory(stagingRoot);
            var rootPrefix = GetRootPrefix(manifestEntry.FullName);
            _logger.LogInformation("Extracting update package with root prefix '{RootPrefix}'.", rootPrefix);
            AppendDesktopLog($"Update: extracting package (root prefix '{rootPrefix}').");
            ExtractArchive(archive, stagingRoot, rootPrefix);

            var computedHash = ComputeContentHash(stagingRoot);
            _logger.LogInformation("Computed update hash {Hash}.", computedHash);
            AppendDesktopLog($"Update: computed hash {computedHash}.");
            if (!string.Equals(computedHash, manifest.Hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdatePackageException("Update package hash mismatch.");
            }

            var manifestPath = Path.Combine(stagingRoot, ManifestFileName);
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }

            var scriptPath = Path.Combine(updatesRoot, $"apply-{updateId}.ps1");
            WriteUpdateScript(scriptPath);

            var exePath = ResolveExePath();
            AppendDesktopLog("Update: launching updater script.");
            LaunchUpdater(scriptPath, stagingRoot, _paths.AppRoot, exePath);

            _ = Task.Run(async () =>
            {
                await Task.Delay(750);
                Environment.Exit(0);
            });

            return manifest.Version;
        }
        catch (Exception ex)
        {
            AppendDesktopLog($"Update failed: {ex.Message}");
            throw;
        }
    }

    private void AppendDesktopLog(string message)
    {
        var logPath = Path.Combine(_paths.DataDirectory, "desktop.log");
        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static ZipArchiveEntry FindManifestEntry(ZipArchive archive)
    {
        var matches = archive.Entries
            .Where(entry => string.Equals(entry.Name, ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            throw new UpdatePackageException("Update package is missing glance.update.json.");
        }

        if (matches.Count > 1)
        {
            throw new UpdatePackageException("Update package contains multiple update manifests.");
        }

        return matches[0];
    }

    private static async Task<UpdateManifest> ReadManifestAsync(ZipArchiveEntry entry, CancellationToken token)
    {
        await using var stream = entry.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = document.RootElement;

        if (!root.TryGetProperty("version", out var versionElement))
        {
            throw new UpdatePackageException("Update manifest is missing a version.");
        }
        if (!root.TryGetProperty("hash", out var hashElement))
        {
            throw new UpdatePackageException("Update manifest is missing a hash.");
        }

        var format = root.TryGetProperty("format", out var formatElement)
            ? formatElement.GetString()
            : string.Empty;
        var algorithm = root.TryGetProperty("algorithm", out var algorithmElement)
            ? algorithmElement.GetString()
            : string.Empty;

        return new UpdateManifest(
            versionElement.GetString() ?? string.Empty,
            hashElement.GetString() ?? string.Empty,
            algorithm ?? string.Empty,
            format ?? string.Empty);
    }

    private static void ValidateManifest(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new UpdatePackageException("Update manifest version is empty.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Hash))
        {
            throw new UpdatePackageException("Update manifest hash is empty.");
        }

        if (!string.Equals(manifest.Format, ManifestFormat, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdatePackageException("Update manifest format is not supported.");
        }

        if (!string.Equals(manifest.Algorithm, HashAlgorithmName, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdatePackageException("Update manifest hash algorithm is not supported.");
        }
    }

    private static DateTimeOffset ParseVersion(string value, string label)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                VersionFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new UpdatePackageException(
                $"Update {label} version '{value}' is not in expected format '{VersionFormat}'.");
        }

        return parsed;
    }

    private static string GetRootPrefix(string manifestPath)
    {
        var normalized = manifestPath.Replace('\\', '/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new UpdatePackageException("Update package contains invalid paths.");
        }

        var lastSlash = normalized.LastIndexOf('/', normalized.Length - 1);
        return lastSlash >= 0 ? normalized[..(lastSlash + 1)] : string.Empty;
    }

    private static void ExtractArchive(ZipArchive archive, string destinationRoot, string rootPrefix)
    {
        var destinationFull = Path.GetFullPath(destinationRoot);
        var destinationPrefix = destinationFull.EndsWith(Path.DirectorySeparatorChar)
            ? destinationFull
            : destinationFull + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var entryPath = entry.FullName.Replace('\\', '/');
            if (!string.IsNullOrEmpty(rootPrefix)
                && !entryPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdatePackageException("Update package contains unexpected root entries.");
            }

            var relative = string.IsNullOrEmpty(rootPrefix)
                ? entryPath
                : entryPath[rootPrefix.Length..];

            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            if (relative.Contains("..", StringComparison.Ordinal))
            {
                throw new UpdatePackageException("Update package contains invalid paths.");
            }

            if (IsForbiddenTopLevel(relative))
            {
                throw new UpdatePackageException("Update package must not include data or blobs.");
            }

            var safeRelative = relative.Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, safeRelative));
            if (!destinationPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdatePackageException("Update package contains invalid paths.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var input = entry.Open();
            using var output = File.Create(destinationPath);
            input.CopyTo(output);
        }
    }

    private static bool IsForbiddenTopLevel(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var firstSegment = normalized.Split('/', 2)[0];
        return ForbiddenTopLevelFolders.Any(folder =>
            string.Equals(firstSegment, folder, StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeContentHash(string rootPath)
    {
        using var sha = SHA256.Create();
        var nullByte = new byte[] { 0 };
        var files = Directory
            .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                FullPath = path,
                Relative = Path.GetRelativePath(rootPath, path).Replace('\\', '/')
            })
            .OrderBy(entry => entry.Relative, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var nameBytes = Encoding.UTF8.GetBytes(file.Relative);
            sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
            sha.TransformBlock(nullByte, 0, nullByte.Length, null, 0);

            var contentBytes = File.ReadAllBytes(file.FullPath);
            sha.TransformBlock(contentBytes, 0, contentBytes.Length, null, 0);
            sha.TransformBlock(nullByte, 0, nullByte.Length, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private void WriteUpdateScript(string scriptPath)
    {
        var script = """
param(
  [int]$pid,
  [string]$source,
  [string]$target,
  [string]$exe
)

try { Wait-Process -Id $pid -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Milliseconds 500

if (!(Test-Path -LiteralPath $source)) { exit 2 }
if (!(Test-Path -LiteralPath $target)) { exit 3 }

Get-ChildItem -LiteralPath $source | Where-Object {
  $_.Name -notin @("data", "blobs")
} | ForEach-Object {
  $destination = Join-Path $target $_.Name
  Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
}

if (Test-Path -LiteralPath $exe) {
  Start-Process -FilePath $exe -WorkingDirectory $target
}

try { Remove-Item -LiteralPath $source -Recurse -Force } catch {}
""";

        File.WriteAllText(scriptPath, script, Encoding.ASCII);
    }

    private void LaunchUpdater(string scriptPath, string stagingRoot, string appRoot, string exePath)
    {
        var pid = Process.GetCurrentProcess().Id;
        var arguments =
            $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -pid {pid} -source \"{stagingRoot}\" -target \"{appRoot}\" -exe \"{exePath}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = arguments,
            WorkingDirectory = appRoot,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        _logger.LogInformation("Starting updater script at {Path}", scriptPath);
        var started = Process.Start(startInfo);
        if (started == null)
        {
            throw new UpdatePackageException("Failed to start the updater process.");
        }
    }

    private string ResolveExePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        var fallback = Path.Combine(_paths.AppRoot, "glance.exe");
        return fallback;
    }

    private sealed record UpdateManifest(string Version, string Hash, string Algorithm, string Format);
}

internal sealed class UpdatePackageException : Exception
{
    public UpdatePackageException(string message) : base(message)
    {
    }
}
