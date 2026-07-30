using Blazored.LocalStorage;

namespace CompanyEmployees.Web.Services;

public class ThemeState(ILocalStorageService localStorage)
{
    private const string StorageKey = "darkMode";

    public bool IsDarkMode { get; private set; }

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        IsDarkMode = await localStorage.GetItemAsync<bool>(StorageKey);
        OnChange?.Invoke();
    }

    public async Task ToggleAsync()
    {
        IsDarkMode = !IsDarkMode;
        await localStorage.SetItemAsync(StorageKey, IsDarkMode);
        OnChange?.Invoke();
    }
}
