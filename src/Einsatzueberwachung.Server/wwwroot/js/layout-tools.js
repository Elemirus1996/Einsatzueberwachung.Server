window.layoutTools = window.layoutTools || {};

window.layoutTools.getSidebarCollapsed = function () {
    const value = localStorage.getItem("einsatz.sidebarCollapsed");
    return value === "1" || value === "true";
};

window.layoutTools.setSidebarCollapsed = function (collapsed) {
    localStorage.setItem("einsatz.sidebarCollapsed", collapsed ? "1" : "0");
};

window.layoutTools.toggleFullscreen = async function () {
    if (!document.fullscreenElement) {
        await document.documentElement.requestFullscreen();
        return;
    }

    await document.exitFullscreen();
};
