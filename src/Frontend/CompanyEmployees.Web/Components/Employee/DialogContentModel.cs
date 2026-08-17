namespace CompanyEmployees.Web.Components.Employee;

/// <summary>
/// Base for the state object a Fluent dialog body binds to.
///
/// Fluent runs the primary action's validation (<c>DialogParameters.ValidateDialogAsync</c>)
/// from the footer, which lives outside the content component's own event loop. Validation
/// that writes a message onto the model therefore updates state nothing is re-rendering, and
/// the dialog just refuses to close in silence. The content component sets
/// <see cref="Refresh"/> to its own re-render, and validation calls
/// <see cref="NotifyChanged"/> after writing the message.
/// </summary>
public abstract class DialogContentModel
{
    /// <summary>Set by the dialog body component; invoking it re-renders the dialog.</summary>
    public Action? Refresh { get; set; }

    protected void NotifyChanged() => Refresh?.Invoke();
}
