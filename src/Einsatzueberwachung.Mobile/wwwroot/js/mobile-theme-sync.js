window.mobileThemeSync = (() => {
    const storageKey = "einsatz.theme";
    const legacyStorageKey = "theme";

    function apply(value) {
        const normalized = value === "dark" ? "dark" : "light";
        document.documentElement.setAttribute("data-bs-theme", normalized);
    }

    function init() {
        const stored = localStorage.getItem(storageKey) || localStorage.getItem(legacyStorageKey);
        apply(stored || "light");

        window.addEventListener("storage", (event) => {
            if ((event.key !== storageKey && event.key !== legacyStorageKey) || !event.newValue) {
                return;
            }

            apply(event.newValue);
        });
    }

    return {
        init
    };
})();
