window.downloadFile = function (fileName, content, mimeType) {
    var blob = new Blob([content], { type: mimeType });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

window.layoutTools = window.layoutTools || {};

window.layoutTools.warningAudio = window.layoutTools.warningAudio || {
    ctx: null,
    repeatId: null,
    activePlayers: []
};

window.layoutTools.setEmbedMode = function (enabled) {
    if (enabled) {
        document.body.classList.add('map-embed-mode');
    } else {
        document.body.classList.remove('map-embed-mode');
    }
};

window.layoutTools.scrollToElement = function (elementId) {
    const el = document.getElementById(elementId);
    if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        el.classList.add('mention-scroll-highlight');
        window.setTimeout(function () { el.classList.remove('mention-scroll-highlight'); }, 2500);
    }
};

window.layoutTools.getSidebarCollapsed = function () {
    const value = localStorage.getItem("einsatz.sidebarCollapsed");
    return value === "1" || value === "true";
};

// Aktuelle Position des Geraets (EL-Startseite: „Meinen Standort verwenden“). Braucht HTTPS.
window.layoutTools.getCurrentPosition = function () {
    return new Promise(function (resolve) {
        if (!navigator.geolocation) {
            resolve({ ok: false, error: "Dieses Gerät kann keinen Standort liefern." });
            return;
        }
        navigator.geolocation.getCurrentPosition(
            function (pos) {
                resolve({ ok: true, lat: pos.coords.latitude, lng: pos.coords.longitude, accuracy: Math.round(pos.coords.accuracy) });
            },
            function (err) {
                var texte = { 1: "Standortfreigabe verweigert – in den Einstellungen des Browsers erlauben.", 2: "Standort nicht verfügbar.", 3: "Standort hat zu lange gedauert." };
                resolve({ ok: false, error: texte[err.code] || err.message });
            },
            { enableHighAccuracy: true, timeout: 15000, maximumAge: 30000 });
    });
};

// Handy (schmaler Bildschirm): beim ersten Aufruf der Startseite in dieser Sitzung zur EL-Ansicht wechseln
window.layoutTools.shouldOpenElOnPhone = function () {
    if (!window.matchMedia("(max-width: 640px)").matches) return false;
    try {
        if (sessionStorage.getItem("einsatz.elOpened") === "1") return false;
        sessionStorage.setItem("einsatz.elOpened", "1");
    } catch (e) {
        // Ohne sessionStorage (privater Modus): trotzdem einmal weiterleiten
    }
    return true;
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

window.layoutTools.getClientLocalNow = function () {
    const now = new Date();
    return {
        year: now.getFullYear(),
        month: now.getMonth() + 1,
        day: now.getDate(),
        hour: now.getHours(),
        minute: now.getMinutes(),
        second: now.getSeconds()
    };
};

window.layoutTools.playWarningAlert = async function (soundType, frequency, volume, repeat, repeatSeconds) {
    const state = window.layoutTools.warningAudio;
    const type = (soundType || "beep").toLowerCase();
    const soundFileUrl = getWarningFileUrl(type);

    if (!soundFileUrl) {
        if (!state.ctx) {
            const AudioContextType = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextType) {
                return false;
            }

            state.ctx = new AudioContextType();
        }

        if (state.ctx.state === "suspended") {
            await state.ctx.resume();
        }
    }

    const playTone = () => {
        const safeFrequency = Math.max(120, Math.min(2200, Number(frequency) || 800));
        const normalizedVolume = Math.max(0, Math.min(1, (Number(volume) || 70) / 100));

        if (soundFileUrl) {
            playWarningFile(soundFileUrl, normalizedVolume);
            return;
        }

        if (type === "siren") {
            ringSweep(state.ctx, safeFrequency, normalizedVolume, 0.85);
            return;
        }

        if (type === "pulse") {
            ringPattern(state.ctx, safeFrequency, normalizedVolume, [0, 0.2, 0.4, 0.6], 0.08, "square");
            return;
        }

        if (type === "chime") {
            ringPattern(state.ctx, safeFrequency, normalizedVolume, [0, 0.12, 0.28], 0.1, "sine", [0, 4, 7]);
            return;
        }

        if (type === "triple") {
            ringPattern(state.ctx, safeFrequency, normalizedVolume, [0, 0.15, 0.31], 0.1, "triangle");
            return;
        }

        if (type === "klaxon") {
            ringPattern(state.ctx, safeFrequency, normalizedVolume, [0, 0.14, 0.28, 0.42], 0.11, "square", [0, -3, 0, -3]);
            return;
        }

        if (type === "rising") {
            ringRising(state.ctx, safeFrequency, normalizedVolume, 0.85);
            return;
        }

        if (type === "double") {
            ringPattern(state.ctx, safeFrequency, normalizedVolume, [0, 0.2], 0.12, "triangle");
            return;
        }

        if (type === "alarm") {
            ringPattern(state.ctx, safeFrequency, normalizedVolume, [0, 0.16, 0.31, 0.52], 0.14, "sawtooth");
            return;
        }

        if (type === "bell") {
            ringPattern(state.ctx, safeFrequency, normalizedVolume, [0, 0.09, 0.19], 0.07, "triangle");
            return;
        }

        ringPattern(state.ctx, safeFrequency, normalizedVolume, [0], 0.17, "square");
    };

    playTone();

    window.layoutTools.stopWarningAlert();
    if (repeat) {
        const safeSeconds = Math.max(1, Number(repeatSeconds) || 30);
        state.repeatId = window.setInterval(playTone, safeSeconds * 1000);
    }

    return true;
};

window.layoutTools.stopWarningAlert = function () {
    const state = window.layoutTools.warningAudio;
    if (state.repeatId) {
        window.clearInterval(state.repeatId);
        state.repeatId = null;
    }

    if (Array.isArray(state.activePlayers) && state.activePlayers.length > 0) {
        state.activePlayers.forEach(player => {
            try {
                player.pause();
                player.currentTime = 0;
            } catch {
                // Ignore player stop errors.
            }
        });
        state.activePlayers = [];
    }
};

function getWarningFileUrl(type) {
    switch (type) {
        case "file_funk":
            return "/audio/warnings/funk.wav";
        case "file_glocke":
            return "/audio/warnings/glocke.wav";
        case "file_kritisch":
            return "/audio/warnings/kritisch.wav";
        default:
            return null;
    }
}

function playWarningFile(url, volume) {
    const state = window.layoutTools.warningAudio;
    const player = new Audio(url);
    player.preload = "auto";
    player.volume = Math.max(0, Math.min(1, Number(volume) || 0.7));

    const removePlayer = () => {
        state.activePlayers = state.activePlayers.filter(entry => entry !== player);
    };

    player.addEventListener("ended", removePlayer, { once: true });
    player.addEventListener("error", removePlayer, { once: true });
    state.activePlayers.push(player);
    player.play().catch(removePlayer);
}

function ringPattern(ctx, baseFrequency, volume, offsets, duration, waveform, semitoneIntervals) {
    offsets.forEach((offset, index) => {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();

        const startAt = ctx.currentTime + offset;
        const stopAt = startAt + duration;

        osc.type = waveform;
        if (Array.isArray(semitoneIntervals) && semitoneIntervals.length > 0) {
            const semitone = semitoneIntervals[index % semitoneIntervals.length];
            osc.frequency.value = baseFrequency * Math.pow(2, semitone / 12);
        } else {
            osc.frequency.value = baseFrequency + index * 60;
        }
        gain.gain.setValueAtTime(0.0001, startAt);
        gain.gain.exponentialRampToValueAtTime(Math.max(0.0001, volume), startAt + 0.01);
        gain.gain.exponentialRampToValueAtTime(0.0001, stopAt);

        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.start(startAt);
        osc.stop(stopAt + 0.02);
    });
}

function ringSweep(ctx, baseFrequency, volume, totalDuration) {
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    const startAt = ctx.currentTime;
    const stopAt = startAt + totalDuration;

    osc.type = "sawtooth";
    osc.frequency.setValueAtTime(baseFrequency * 0.75, startAt);
    osc.frequency.linearRampToValueAtTime(baseFrequency * 1.45, startAt + totalDuration * 0.5);
    osc.frequency.linearRampToValueAtTime(baseFrequency * 0.7, stopAt);

    gain.gain.setValueAtTime(0.0001, startAt);
    gain.gain.exponentialRampToValueAtTime(Math.max(0.0001, volume), startAt + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, stopAt);

    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start(startAt);
    osc.stop(stopAt + 0.03);
}

function ringRising(ctx, baseFrequency, volume, totalDuration) {
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    const startAt = ctx.currentTime;
    const stopAt = startAt + totalDuration;

    osc.type = "triangle";
    osc.frequency.setValueAtTime(baseFrequency * 0.7, startAt);
    osc.frequency.exponentialRampToValueAtTime(baseFrequency * 1.8, stopAt);

    gain.gain.setValueAtTime(0.0001, startAt);
    gain.gain.exponentialRampToValueAtTime(Math.max(0.0001, volume), startAt + 0.03);
    gain.gain.exponentialRampToValueAtTime(0.0001, stopAt);

    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start(startAt);
    osc.stop(stopAt + 0.03);
}

// Scrolls an element into view by id. Returns true when found, false otherwise.
// Seit Release 1.7.4 hat der Einsatz-Monitor pro Panel einen eigenen Scrollbalken
// (.dashboard-panel-body, overflow-y:auto) statt Seiten-Scrolling. Ein reines
// scrollIntoView reicht dann nicht zuverlässig, daher zuerst den inneren
// Panel-Scroller direkt setzen und scrollIntoView als Fallback nutzen.
window.warnToastScrollTo = function (elementId) {
    var el = document.getElementById(elementId);
    if (!el) return false;

    var scroller = el.closest('.dashboard-panel-body');
    if (scroller) {
        var top = el.offsetTop - scroller.clientHeight / 2 + el.clientHeight / 2;
        scroller.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });
    }

    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    return true;
};
