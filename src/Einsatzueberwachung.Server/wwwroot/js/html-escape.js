// Text sicher in HTML einsetzen (Leaflet-Popups, Tooltips, Marker-Icons).
// Namen von Teams, Hunden, Halsbaendern, Suchgebieten und Punkten sind Freitext bzw. kommen ueber den
// Halsband-Webhook von aussen – ohne Escaping wuerden sie als HTML/Script ausgefuehrt (Audit 2026-10-24).
window.escapeHtml = function (value) {
    if (value === null || value === undefined) return '';
    return String(value)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
};
