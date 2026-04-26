namespace Vanadium.Note.Web.Services;

public class ThemeService
{
    public bool IsDarkMode { get; private set; }
    public event Action? OnChanged;

    public void SetDarkMode(bool isDark)
    {
        IsDarkMode = isDark;
        OnChanged?.Invoke();
    }
}
