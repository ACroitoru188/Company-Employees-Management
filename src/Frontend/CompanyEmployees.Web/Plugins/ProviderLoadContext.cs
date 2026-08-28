using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace CompanyEmployees.Web.Plugins;

/// <summary>
/// Isolated load context for a database provider plugin.
/// </summary>
internal sealed class ProviderLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;

    public ProviderLoadContext(string providerDllPath)
        : base(name: Path.GetFileNameWithoutExtension(providerDllPath), isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(providerDllPath);
        _pluginDirectory = Path.GetDirectoryName(providerDllPath) ?? string.Empty;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && IsSharedAssembly(assemblyName.Name))
            return null; // Delegate to host (Default) context to preserve type identity

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path is not null && File.Exists(path))
            return LoadUnmanagedDllFromPath(path);

        if (!string.IsNullOrEmpty(_pluginDirectory))
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                Architecture.X86 => "win-x86",
                _ => "win-x64"
            };

            var exactCandidate = Path.Combine(_pluginDirectory, "runtimes", arch, "native", $"{unmanagedDllName}.dll");
            if (File.Exists(exactCandidate))
                return LoadUnmanagedDllFromPath(exactCandidate);

            var sniCandidate = Path.Combine(_pluginDirectory, "runtimes", arch, "native", "Microsoft.Data.SqlClient.SNI.dll");
            if (File.Exists(sniCandidate))
                return LoadUnmanagedDllFromPath(sniCandidate);
        }

        return IntPtr.Zero;
    }

    private static bool IsSharedAssembly(string name)
    {
        if (name.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Microsoft.EntityFrameworkCore.Relational", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Microsoft.EntityFrameworkCore.Abstractions", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CompanyEmployees.Persistence.Contracts", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CompanyEmployees.Persistence", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CompanyEmployees.Domain", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
