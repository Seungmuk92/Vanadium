// Theme bootstrap. Kept as an external (non-module) script so the functions stay
// on the global scope for Blazor JS interop (MainLayout/Settings call applyTheme,
// storeTheme, setDarkMode, getSystemDarkMode). Extracted out of index.html so the
// page can ship a Content-Security-Policy whose script-src omits 'unsafe-inline'
// (issue #199) — an inline <script> block would otherwise be blocked.
function setDarkMode(isDark) {
    document.documentElement.setAttribute('data-bs-theme', isDark ? 'dark' : 'light');
    document.body.classList.toggle('dark-mode', isDark);
}
function getSystemDarkMode() {
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
}
function applyTheme(theme) {
    if (theme === 'dark') setDarkMode(true);
    else if (theme === 'light') setDarkMode(false);
    else setDarkMode(getSystemDarkMode());
}
function getStoredTheme() {
    return localStorage.getItem('vn-theme') || 'system';
}
function storeTheme(theme) {
    localStorage.setItem('vn-theme', theme);
}
// Apply immediately to prevent flash before Blazor loads
applyTheme(getStoredTheme());
