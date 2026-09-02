using System.Globalization;

namespace CompanyEmployees.Web.Services;

/// <summary>
/// The language of the circuit this scope belongs to, kept so a render started from somewhere
/// else can put it back.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppLocalizer"/> resolves every label from <see cref="CultureInfo.CurrentUICulture"/>
/// at render time, and that is ambient state carried by the execution context. A render started
/// by the circuit (a click, a navigation, the first load) runs under the culture request
/// localization assigned, so it is translated. A render started from somewhere else — the
/// database monitor's background timer, the notification dispatcher publishing from the thread
/// that saved the row — runs under *that* context's culture instead, which is the process
/// default. <c>InvokeAsync</c> moves the work onto the circuit's dispatcher but does not restore
/// its culture, so those renders silently fall back to English: the drawer and the app bar
/// flipped back a couple of seconds after every page load while the page itself stayed
/// translated.
/// </para>
/// <para>
/// Scoped, so one instance per circuit. Any handler that renders in response to something
/// outside the circuit must call <see cref="Restore"/> before touching component state.
/// </para>
/// </remarks>
public sealed class CircuitCulture
{
    // Captured on construction as well as through Capture(): the scope resolves this service
    // while the circuit is rendering, so even a caller that forgets to capture gets the
    // circuit's culture rather than the process default.
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    /// <summary>Records the culture of the caller. Call from the circuit, never from a callback.</summary>
    public void Capture()
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
    }

    /// <summary>Applies the recorded culture to the caller.</summary>
    public void Restore()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
