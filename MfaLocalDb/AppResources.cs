using System.Reflection;

namespace MfaLocalDb;

internal static class AppResources
{
    private const string DatabaseFileName = "mfa-local.db";
    private const string MapFileName = "ne_110m_admin_0_countries.geojson";

    public static string DataDirectory { get; private set; } = string.Empty;

    public static string DatabasePath { get; private set; } = string.Empty;

    public static string MapPath { get; private set; } = string.Empty;

    public static void Prepare()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MfaLocalDb");
        var assetDirectory = Path.Combine(DataDirectory, "Assets");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(assetDirectory);

        DatabasePath = Path.Combine(DataDirectory, DatabaseFileName);
        MapPath = Path.Combine(assetDirectory, MapFileName);

        ExtractResource(DatabaseFileName, DatabasePath);
        ExtractResource(MapFileName, MapPath);
    }

    private static void ExtractResource(string resourceSuffix, string targetPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new FileNotFoundException($"内置资源不存在：{resourceSuffix}");
        }

        using var source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"无法读取内置资源：{resourceName}");

        var shouldWrite = !File.Exists(targetPath) || new FileInfo(targetPath).Length != source.Length;
        if (!shouldWrite)
        {
            return;
        }

        using var target = File.Create(targetPath);
        source.CopyTo(target);
    }
}
