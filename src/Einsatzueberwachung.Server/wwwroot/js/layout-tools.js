window.layoutTools = window.layoutTools || {};

window.layoutTools.warningAudio = window.layoutTools.warningAudio || {
    ctx: null,
    repeatId: null
};

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

    const playTone = () => {
        const safeFrequency = Math.max(120, Math.min(2200, Number(frequency) || 800));
        const normalizedVolume = Math.max(0, Math.min(1, (Number(volume) || 70) / 100));
        const type = (soundType || "beep").toLowerCase();

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
};

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
