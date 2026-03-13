using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

const string DefaultInstallSubFolder = "Programs\\Pclt.EidAttendance";
const string ManifestConfigFileName = "update-manifest-url.txt";
const string DefaultManifestUrl = "https://raw.githubusercontent.com/pclt-codebase/PCLT-eIDattendance/main/latest.json";

var arguments = ParseArgs(args);
var manifestUrl = ResolveManifestUrl(arguments.ManifestUrlArg);
if (string.IsNullOrWhiteSpace(manifestUrl))
{
    ShowError("Geen geldige update-manifest URL gevonden.", arguments.Silent);
    return 1;
}

var installDir = string.IsNullOrWhiteSpace(arguments.InstallDirArg)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DefaultInstallSubFolder)
    : arguments.InstallDirArg!;

try
{
    await InstallAsync(installDir, manifestUrl, arguments.Silent);

    ShowInfo(
        "Installatie voltooid.\n\nDe app staat klaar via Desktop/Startmenu: PCLT eID Attendance.",
        arguments.Silent);
    return 0;
}
catch (Exception ex)
{
    ShowError($"Installatie mislukt:\n\n{ex.Message}", arguments.Silent);
    return 1;
}

static (string? ManifestUrlArg, string? InstallDirArg, bool Silent) ParseArgs(string[] args)
{
    string? manifestUrl = null;
    string? installDir = null;
    var silent = false;

    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--manifest-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            manifestUrl = args[++i];
            continue;
        }

        if (string.Equals(args[i], "--install-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            installDir = args[++i];
            continue;
        }

        if (string.Equals(args[i], "--silent", StringComparison.OrdinalIgnoreCase))
        {
            silent = true;
        }
    }

    return (manifestUrl, installDir, silent);
}

static string ResolveManifestUrl(string? manifestUrlArg)
{
    if (!string.IsNullOrWhiteSpace(manifestUrlArg))
    {
        return manifestUrlArg.Trim();
    }

    var localConfig = Path.Combine(AppContext.BaseDirectory, ManifestConfigFileName);
    if (File.Exists(localConfig))
    {
        var value = ReadFirstConfigValue(localConfig);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    var fromEnvironment = Environment.GetEnvironmentVariable("EID_UPDATE_MANIFEST_URL");
    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        return fromEnvironment.Trim();
    }

    return DefaultManifestUrl;
}

static string? ReadFirstConfigValue(string filePath)
{
    foreach (var line in File.ReadLines(filePath))
    {
        var value = line.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("#", StringComparison.Ordinal))
        {
            continue;
        }

        return value;
    }

    return null;
}

static async Task InstallAsync(string installDir, string manifestUrl, bool silent)
{
    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    ShowInfo("Installatie gestart. Pakketgegevens worden opgehaald...", silent);
    var manifestResponse = await httpClient.GetAsync(manifestUrl);
    manifestResponse.EnsureSuccessStatusCode();

    var manifestJson = await manifestResponse.Content.ReadAsStringAsync();
    var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

    if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Url))
    {
        throw new InvalidOperationException("Manifest is ongeldig. Vereist: version + url");
    }

    if (manifest.Url.Contains("YOUR-HOST", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Manifest bevat nog een placeholder-URL (YOUR-HOST). Publiceer eerst een geldige pakket-URL.");
    }

    var tempRoot = Path.Combine(Path.GetTempPath(), "eidattendance-installer-" + Guid.NewGuid().ToString("N"));
    var zipPath = Path.Combine(tempRoot, "package.zip");
    var extractPath = Path.Combine(tempRoot, "extract");

    Directory.CreateDirectory(tempRoot);
    Directory.CreateDirectory(extractPath);

    try
    {
        ShowInfo("Installatiepakket wordt gedownload...", silent);
        using (var zipResponse = await httpClient.GetAsync(manifest.Url))
        {
            zipResponse.EnsureSuccessStatusCode();
            await using var zipStream = await zipResponse.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(zipPath);
            await zipStream.CopyToAsync(fileStream);
        }

        ShowInfo("Installatiepakket wordt uitgepakt...", silent);
        ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

        ValidatePackage(extractPath);

        Directory.CreateDirectory(installDir);

        ShowInfo("Bestanden worden geïnstalleerd...", silent);
        CopyDirectory(extractPath, installDir);

        File.WriteAllText(Path.Combine(installDir, "version.txt"), manifest.Version);
        File.WriteAllText(Path.Combine(installDir, ManifestConfigFileName), manifestUrl + Environment.NewLine);

        ShowInfo("Snelkoppelingen worden aangemaakt...", silent);
        CreateShortcuts(installDir);
    }
    finally
    {
        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
        }
    }
}

static void ValidatePackage(string extractPath)
{
    var requiredFiles = new[]
    {
        "Pclt.EidAttendance.App.exe",
        "run-eid.cmd",
        "update-eid.cmd",
        "update-eid.ps1",
        "update-manifest-url.txt"
    };

    foreach (var file in requiredFiles)
    {
        var fullPath = Path.Combine(extractPath, file);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Installatiepakket is onvolledig: ontbrekend bestand '{file}'.");
        }
    }
}

static void CopyDirectory(string sourceDir, string destinationDir)
{
    foreach (var directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(sourceDir, directory);
        Directory.CreateDirectory(Path.Combine(destinationDir, relative));
    }

    foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(sourceDir, file);
        var destinationPath = Path.Combine(destinationDir, relative);
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(file, destinationPath, overwrite: true);
    }
}

static void CreateShortcuts(string installDir)
{
    var runScriptPath = Path.Combine(installDir, "run-eid.cmd");
    if (!File.Exists(runScriptPath))
    {
        return;
    }

    var iconPath = Path.Combine(installDir, "app.ico");
    var exeIconFallbackPath = Path.Combine(installDir, "Pclt.EidAttendance.App.exe");

    var desktopShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "PCLT eID Attendance.lnk");

    var startMenuPrograms = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        "Programs");
    Directory.CreateDirectory(startMenuPrograms);

    var startMenuShortcutPath = Path.Combine(startMenuPrograms, "PCLT eID Attendance.lnk");

    CreateShortcut(desktopShortcutPath, runScriptPath, installDir, iconPath, exeIconFallbackPath);
    CreateShortcut(startMenuShortcutPath, runScriptPath, installDir, iconPath, exeIconFallbackPath);
}

static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string iconPath, string exeIconFallbackPath)
{
    var shellType = Type.GetTypeFromProgID("WScript.Shell");
    if (shellType is null)
    {
        return;
    }

    var shell = Activator.CreateInstance(shellType);
    if (shell is null)
    {
        return;
    }

    try
    {
        var shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
        if (shortcut is null)
        {
            return;
        }

        var shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
        shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });

        var iconLocation = File.Exists(iconPath)
            ? iconPath
            : exeIconFallbackPath + ",0";
        shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { iconLocation });
        shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
    }
    finally
    {
        try
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        catch
        {
        }
    }
}

static void ShowInfo(string message, bool silent)
{
    if (silent)
    {
        return;
    }

    MessageBox.Show(message, "PCLT eID Attendance Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
}

static void ShowError(string message, bool silent)
{
    if (silent)
    {
        return;
    }

    MessageBox.Show(message, "PCLT eID Attendance Installer", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

internal sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
