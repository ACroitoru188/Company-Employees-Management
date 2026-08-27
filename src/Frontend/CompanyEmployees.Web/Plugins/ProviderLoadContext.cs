using System.Reflection;
using System.Runtime.Loader;
using CompanyEmployees.Persistence.Contracts;

namespace CompanyEmployees.Web.Plugins;

/// <summary>
/// Isolated load context for a database provider plugin.
/// </summary>
internal sealed class ProviderLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    // Assemblies that must stay unified with the host
    private static readonly HashSet<string> SharedAssemblies =
    [
        "CompanyEmployees.Persistence.Contracts",
        "CompanyEmployees.Persistence",
        "CompanyEmployees.Domain",
        "Microsoft.EntityFrameworkCore",
    ];

    public ProviderLoadContext(string providerDllPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(providerDllPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && SharedAssemblies.Contains(assemblyName.Name))
            return null; // fall through to the default (host) context

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
