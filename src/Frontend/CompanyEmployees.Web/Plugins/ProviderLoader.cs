using System.Reflection;
using System.Runtime.Loader;
using CompanyEmployees.Persistence.Contracts;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Web.Plugins;

/// <summary>
/// Scans Providers/ directory and loads provider plugins into isolated contexts.
/// </summary>
public static class ProviderLoader
{
    private static readonly List<Assembly> LoadedPluginAssemblies = [];
    private static bool _resolvingHooked;

    public static IReadOnlyList<IDbProviderPlugin> Load(
        string contentRootPath,
        ILogger? logger = null)
    {
        var providersRoot = Path.Combine(contentRootPath, "Providers");
        if (!Directory.Exists(providersRoot))
            providersRoot = Path.Combine(AppContext.BaseDirectory, "Providers");

        if (!Directory.Exists(providersRoot))
        {
            logger?.LogWarning("Providers directory not found at {Path} or in base directory.", providersRoot);
            return [];
        }

        var plugins = new List<IDbProviderPlugin>();

        foreach (var subDir in Directory.EnumerateDirectories(providersRoot))
        {
            var dirName = Path.GetFileName(subDir);
            var dlls = Directory.GetFiles(subDir, "*.dll", SearchOption.TopDirectoryOnly);
            if (dlls.Length == 0)
            {
                logger?.LogWarning("Provider directory {Dir} contains no DLLs — skipped.", dirName);
                continue;
            }

            var entryDll = dlls.FirstOrDefault(dll =>
                Path.GetFileNameWithoutExtension(dll).Contains("Provider", StringComparison.OrdinalIgnoreCase))
                ?? dlls[0];

            try
            {
                var ctx = new ProviderLoadContext(entryDll);
                var assembly = ctx.LoadFromAssemblyPath(entryDll);
                LoadedPluginAssemblies.Add(assembly);

                var pluginTypes = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface &&
                                t.IsAssignableTo(typeof(IDbProviderPlugin)))
                    .ToList();

                if (pluginTypes.Count == 0)
                {
                    logger?.LogWarning(
                        "No IDbProviderPlugin implementation found in {Dll} — skipped.",
                        Path.GetFileName(entryDll));
                    continue;
                }

                foreach (var type in pluginTypes)
                {
                    var instance = (IDbProviderPlugin)Activator.CreateInstance(type)!;
                    plugins.Add(instance);
                    logger?.LogInformation(
                        "Loaded provider plugin: {Id} ({DisplayName}) from {Dir}.",
                        instance.Id,
                        instance.DisplayName,
                        dirName);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "Failed to load provider plugin from {Dir} — it will not be available.",
                    dirName);
            }
        }

        if (!_resolvingHooked && LoadedPluginAssemblies.Count > 0)
        {
            _resolvingHooked = true;
            AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
            {
                return LoadedPluginAssemblies.FirstOrDefault(a =>
                    string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            };
        }

        return plugins.AsReadOnly();
    }
}
