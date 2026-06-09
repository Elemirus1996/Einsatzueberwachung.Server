window.themeSync = (() => {
    const storageKey = "einsatz.theme";
    const legacyStorageKey = "theme";
    const browserPrefsKey = "browser-prefs";

            const themePalette = {
        nrw: { primary: "#A72920", secondary: "#404040", tertiary: "#E3000F", quaternary: "#5BB969" },
        ruhr: { primary: "#005D9E", secondary: "#D6D6D6", tertiary: "#DDED00", quaternary: "#FF7B00" }
    };

    const defaultState = {
        isDark: false,
        preset: "nrw",
        intensity: "ausgewogen"
    };

    let dotNetRef = null;
    let storageHandler = null;
    let systemMediaQuery = null;
    let systemThemeHandler = null;
    let currentState = { ...defaultState };

    function findCustomTheme(customThemes, preset) {
        if (!Array.isArray(customThemes)) {
            return null;
        }

        return customThemes.find(t =>
            t && ((t.id ?? t.Id) === preset));
    }

    function normalizePreset(value) {
        if (typeof value !== "string") {
            return defaultState.preset;
        }

        const normalized = value.trim().toLowerCase();
        // Allow anything that is not nrw or ruhr to be returned as-is
        if (normalized === "ruhr" || normalized === "nrw") {
            return normalized;
        }

        return value.trim();
    }

    function normalizeIntensity(value) {
        if (typeof value !== "string") {
            return defaultState.intensity;
        }

        const normalized = value.trim().toLowerCase();
        if (normalized === "dezent") {
            return "dezent";
        }

        if (normalized === "lebhaft") {
            return "lebhaft";
        }

        return "ausgewogen";
    }

    function canonicalPreset(value) {
        const normalized = normalizePreset(value);
        return normalized === "ruhr" ? "Ruhr" : (normalized === "nrw" ? "NRW" : value);
    }

    function canonicalIntensity(value) {
        const normalized = normalizeIntensity(value);
        if (normalized === "dezent") {
            return "Dezent";
        }

        if (normalized === "lebhaft") {
            return "Lebhaft";
        }

        return "Ausgewogen";
    }

    function setAttributeOnThemeRoots(name, value) {
        document.documentElement.setAttribute(name, value);
        if (document.body) {
            document.body.setAttribute(name, value);
        }
    }

    function setStyleVariableOnThemeRoots(name, value) {
        document.documentElement.style.setProperty(name, value);
        if (document.body) {
            document.body.style.setProperty(name, value);
        }
    }

    function resolveThemeState(input) {
        const resolved = {
            isDark: typeof input?.isDark === "boolean" ? input.isDark : currentState.isDark,
            preset: normalizePreset(input?.preset ?? currentState.preset),
            intensity: normalizeIntensity(input?.intensity ?? currentState.intensity)
        };

        currentState = resolved;
        return resolved;
    }

    function applyThemeState(themeState) {
        const resolved = resolveThemeState(themeState);
        const value = resolved.isDark ? "dark" : "light";

        let palette = themePalette[resolved.preset] || themePalette.nrw;

        // Check custom themes in local storage or server bootstrap state
        try {
             const raw = localStorage.getItem(browserPrefsKey);
             const serverThemes = Array.isArray(window.__einsatzServerCustomThemes)
                 ? window.__einsatzServerCustomThemes
                 : [];
             if (raw) {
                 const parsed = JSON.parse(raw);
                 const customThemes = parsed.customThemes || parsed.CustomThemes;
                 const customTheme = findCustomTheme(customThemes, resolved.preset)
                     ?? findCustomTheme(serverThemes, resolved.preset);
                 if (customTheme) {
                     palette = {
                         primary: customTheme.primaryColor || customTheme.PrimaryColor,
                         secondary: customTheme.secondaryColor || customTheme.SecondaryColor,
                         tertiary: customTheme.tertiaryColor || customTheme.TertiaryColor,
                         quaternary: customTheme.quaternaryColor || customTheme.QuaternaryColor
                     };
                 }
             } else {
                 const customTheme = findCustomTheme(serverThemes, resolved.preset);
                 if (customTheme) {
                     palette = {
                         primary: customTheme.primaryColor || customTheme.PrimaryColor,
                         secondary: customTheme.secondaryColor || customTheme.SecondaryColor,
                         tertiary: customTheme.tertiaryColor || customTheme.TertiaryColor,
                         quaternary: customTheme.quaternaryColor || customTheme.QuaternaryColor
                     };
                 }
             }
        } catch (e) {
            console.error("Error loading custom themes", e);
        }

        setAttributeOnThemeRoots("data-bs-theme", value);
        setAttributeOnThemeRoots("data-theme-preset", resolved.preset);
        setAttributeOnThemeRoots("data-intensity", resolved.intensity);

        setStyleVariableOnThemeRoots("--theme-primary", palette.primary);
        setStyleVariableOnThemeRoots("--theme-secondary", palette.secondary);
        if (palette.tertiary) {
            setStyleVariableOnThemeRoots("--theme-tertiary", palette.tertiary);
            setStyleVariableOnThemeRoots("--tertiary-color", palette.tertiary);
        } else {
            document.documentElement.style.removeProperty('--theme-tertiary');
            document.documentElement.style.removeProperty('--tertiary-color');
            if (document.body) {
                document.body.style.removeProperty('--theme-tertiary');
                document.body.style.removeProperty('--tertiary-color');
            }
        }
        if (palette.quaternary) {
            setStyleVariableOnThemeRoots("--theme-quaternary", palette.quaternary);
            setStyleVariableOnThemeRoots("--quaternary-color", palette.quaternary);
        } else {
            document.documentElement.style.removeProperty('--theme-quaternary');
            document.documentElement.style.removeProperty('--quaternary-color');
            if (document.body) {
                document.body.style.removeProperty('--theme-quaternary');
                document.body.style.removeProperty('--quaternary-color');
            }
        }
        setStyleVariableOnThemeRoots("--primary-color", palette.primary);
        setStyleVariableOnThemeRoots("--secondary-color", palette.secondary);
    }

    function emitThemeChanged() {
        window.dispatchEvent(new CustomEvent("einsatz:theme-changed", {
            detail: {
                isDark: Boolean(currentState.isDark),
                preset: canonicalPreset(currentState.preset),
                intensity: canonicalIntensity(currentState.intensity)
            }
        }));
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

    function parseStateFromBrowserPrefs(value) {
        if (!value || typeof value !== "string") {
            return null;
        }

        try {
            const parsed = JSON.parse(value);
            return {
                isDark: Boolean(parsed?.isDarkMode ?? parsed?.IsDarkMode),
                preset: normalizePreset(parsed?.themePreset ?? parsed?.ThemePreset),
                intensity: normalizeIntensity(parsed?.visualIntensity ?? parsed?.VisualIntensity)
            };
        } catch {
            return null;
        }
    }

    function getStoredBrowserThemeState() {
        const raw = localStorage.getItem(browserPrefsKey);
        return parseStateFromBrowserPrefs(raw);
    }

    function notifyDotNet(isDark) {
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync("OnThemeChangedFromStorage", isDark);
        }
    }

    function applyAndPersist(themeState, notify = true) {
        applyThemeState(themeState);
        localStorage.setItem(storageKey, currentState.isDark ? "dark" : "light");
        emitThemeChanged();

        if (notify) {
            notifyDotNet(currentState.isDark);
        }
    }

    function init(ref, initialIsDark, initialPreset, initialIntensity) {
        dotNetRef = ref;

        const stored = getStoredTheme();
        const browserState = getStoredBrowserThemeState();
        const isDark = browserState
            ? browserState.isDark
            : (stored === null ? Boolean(initialIsDark) : parseStoredTheme(stored));
        const preset = browserState?.preset ?? normalizePreset(initialPreset);
        const intensity = browserState?.intensity ?? normalizeIntensity(initialIntensity);

        applyAndPersist({ isDark, preset, intensity }, false);

        storageHandler = (event) => {
            if (event.newValue === null) {
                return;
            }

            if (event.key === browserPrefsKey) {
                const browserPrefsState = parseStateFromBrowserPrefs(event.newValue);
                if (!browserPrefsState) {
                    return;
                }

                applyAndPersist(browserPrefsState);
                return;
            }

            if (event.key !== storageKey && event.key !== legacyStorageKey) {
                return;
            }

            const changedIsDark = parseStoredTheme(event.newValue);
            applyAndPersist({
                isDark: changedIsDark,
                preset: currentState.preset,
                intensity: currentState.intensity
            });
        };

        window.addEventListener("storage", storageHandler);
    }

    function setTheme(isDark) {
        applyAndPersist({
            isDark: Boolean(isDark),
            preset: currentState.preset,
            intensity: currentState.intensity
        });
    }

    function setThemeState(themeState) {
        applyAndPersist(themeState);
    }

    function stopWatchingSystemTheme() {
        if (systemMediaQuery && systemThemeHandler) {
            systemMediaQuery.removeEventListener("change", systemThemeHandler);
        }
        systemMediaQuery = null;
        systemThemeHandler = null;
    }

    function watchSystemTheme() {
        stopWatchingSystemTheme();

        systemMediaQuery = window.matchMedia("(prefers-color-scheme: dark)");

        // Apply immediately based on current OS preference
        const isDark = systemMediaQuery.matches;
        applyAndPersist({
            isDark,
            preset: currentState.preset,
            intensity: currentState.intensity
        });

        // Listen for future OS preference changes
        systemThemeHandler = (event) => {
            const changed = event.matches;
            applyAndPersist({
                isDark: changed,
                preset: currentState.preset,
                intensity: currentState.intensity
            });
        };

        systemMediaQuery.addEventListener("change", systemThemeHandler);
    }

    function dispose() {
        if (storageHandler) {
            window.removeEventListener("storage", storageHandler);
            storageHandler = null;
        }

        stopWatchingSystemTheme();
        dotNetRef = null;
    }

    return {
        init,
        setTheme,
        setThemeState,
        watchSystemTheme,
        stopWatchingSystemTheme,
        dispose
    };
})();
