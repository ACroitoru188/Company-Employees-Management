using Bunit;
using CompanyEmployees.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;

namespace CompanyEmployees.Web.Tests;

/// <summary>
/// Shared bUnit setup for components built on Fluent UI.
/// </summary>
/// <remarks>
/// Two things every one of them needs. Fluent resolves its own services, so without
/// <c>AddFluentUIComponents</c> the render throws before any assertion runs; and its components
/// call into JS while initialising, so JSInterop runs loose — nothing here asserts on those
/// calls, and strict mode would only turn them into noise.
///
/// <see cref="AppLocalizer"/> is registered against the real <c>Web/Languages</c> folder rather
/// than a stub: it is the singleton the app builds at startup, and a component asking for a key
/// that does not ship should fail here the same way it would fail in the browser.
/// </remarks>
public abstract class WebTestContext : TestContext
{
    protected WebTestContext()
    {
        Services.AddFluentUIComponents();

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(WebContentRoot);
        Services.AddSingleton(new AppLocalizer(environment));

        // Derived fixtures register here rather than in their own constructor: bUnit locks the
        // service provider the moment anything is resolved from it, and touching JSInterop
        // below does exactly that. C# runs a derived class's field initialisers before this
        // constructor, so their substitutes already exist by the time this is called.
        RegisterServices(Services);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>Register test doubles the component under test needs.</summary>
    protected virtual void RegisterServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// The Web project folder, found by walking up to the repository root rather than by a
    /// hard-coded depth, so it survives a change of target framework or output layout.
    /// </summary>
    protected static string WebContentRoot { get; } = FindWebContentRoot();

    private static string FindWebContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CompanyEmployees.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException(
                "Could not find the repository root from the test output folder.");

        return Path.Combine(directory.FullName, "src", "Frontend", "CompanyEmployees.Web");
    }
}
