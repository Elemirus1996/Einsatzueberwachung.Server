// Bindet HLS-Streams (.m3u8) an <video>-Elemente. Nutzt natives HLS in Safari,
// sonst dynamisch nachgeladenes hls.js (lokal, wwwroot/lib/hls).
(function () {
    const HLS_CDN = "/lib/hls/hls.min.js";
    let hlsLoader = null;

    function loadHlsLib() {
        if (window.Hls) return Promise.resolve(window.Hls);
        if (hlsLoader) return hlsLoader;
        hlsLoader = new Promise((resolve, reject) => {
            const s = document.createElement("script");
            s.src = HLS_CDN;
            s.async = true;
            s.onload = () => resolve(window.Hls);
            s.onerror = () => reject(new Error("hls.js Laden fehlgeschlagen"));
            document.head.appendChild(s);
        });
        return hlsLoader;
    }

    function attach(videoEl, url) {
        if (!videoEl || !url) return;
        // Bestehende Instanz aufraeumen, falls Re-Render
        if (videoEl.__hlsInstance) {
            try { videoEl.__hlsInstance.destroy(); } catch (e) { /* ignore */ }
            videoEl.__hlsInstance = null;
        }
        if (videoEl.canPlayType("application/vnd.apple.mpegurl")) {
            videoEl.src = url;
            videoEl.play().catch(() => { /* autoplay-block ignorieren */ });
            return;
        }
        loadHlsLib().then((Hls) => {
            if (!Hls || !Hls.isSupported()) {
                videoEl.src = url;
                return;
            }
            const hls = new Hls({ liveDurationInfinity: true });
            hls.loadSource(url);
            hls.attachMedia(videoEl);
            videoEl.__hlsInstance = hls;
            videoEl.play().catch(() => { /* autoplay-block ignorieren */ });
        }).catch((err) => {
            console.warn("DroneStream HLS-Fallback:", err);
            videoEl.src = url;
        });
    }

    function detach(videoEl) {
        if (!videoEl) return;
        if (videoEl.__hlsInstance) {
            try { videoEl.__hlsInstance.destroy(); } catch (e) { /* ignore */ }
            videoEl.__hlsInstance = null;
        }
        videoEl.removeAttribute("src");
        videoEl.load();
    }

    // Bindet alle <video data-hls-src="..."> innerhalb eines Containers an HLS.
    function attachAllInContainer(container) {
        if (!container) return;
        const videos = container.querySelectorAll("video[data-hls-src]");
        videos.forEach((v) => attach(v, v.getAttribute("data-hls-src")));
    }

    window.DroneStream = { attach, detach, attachAllInContainer };
})();
