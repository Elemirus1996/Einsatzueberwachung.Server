window.themeSync = (() => {
    const storageKey = "einsatz.theme";
    const legacyStorageKey = "theme";
    let dotNetRef = null;
    let storageHandler = null;

    function applyTheme(isDark) {
        const value = isDark ? "dark" : "light";
        document.documentElement.setAttribute("data-bs-theme", value);
    }

    function parseStoredTheme(value) {
        if (typeof value === "boolean") {
            return value;
        }

        if (value === null || value === undefined) {
            return false;
        }

        const normalized = String(value).trim().toLowerCase();
        return normalized === "dark"
            || normalized === "true"
            || normalized === "1"
            || normalized === "yes"
            || normalized === "on";
    }

    function getStoredTheme() {
        return localStorage.getItem(storageKey) || localStorage.getItem(legacyStorageKey);
    }

    function init(ref, initialIsDark) {
        dotNetRef = ref;

        const stored = getStoredTheme();
        const isDark = stored === null ? Boolean(initialIsDark) : parseStoredTheme(stored);

        applyTheme(isDark);
        localStorage.setItem(storageKey, isDark ? "dark" : "light");

        storageHandler = (event) => {
            if ((event.key !== storageKey && event.key !== legacyStorageKey) || event.newValue === null) {
                return;
            }

            const changedIsDark = parseStoredTheme(event.newValue);
            applyTheme(changedIsDark);

            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("OnThemeChangedFromStorage", changedIsDark);
            }
        };

        window.addEventListener("storage", storageHandler);
    }

    function setTheme(isDark) {
        applyTheme(isDark);
        localStorage.setItem(storageKey, isDark ? "dark" : "light");

        if (dotNetRef) {
            dotNetRef.invokeMethodAsync("OnThemeChangedFromStorage", isDark);
        }
    }

    function dispose() {
        if (storageHandler) {
            window.removeEventListener("storage", storageHandler);
            storageHandler = null;
        }

        dotNetRef = null;
    }

    return {
        init,
        setTheme,
        dispose
    };
})();
