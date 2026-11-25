(function () {
    let initialized = false;
    // Schalter „Tastenkürzel ausschalten“ in den Einstellungen (gilt fuer alle Seiten dieses Browsers)
    let _aus = false;
    let _cfg = {
        navHome:    'ctrl+h',
        navKarte:   'ctrl+m',
        navMonitor: 'ctrl+n',
        navStart:   'ctrl+t'
    };

    function parse(str) {
        const p = str.toLowerCase().split('+');
        return {
            ctrl:  p.includes('ctrl'),
            shift: p.includes('shift'),
            alt:   p.includes('alt'),
            key:   p[p.length - 1]
        };
    }

    function matches(event, str) {
        const s = parse(str);
        return event.key.toLowerCase() === s.key
            && event.ctrlKey  === s.ctrl
            && event.shiftKey === s.shift
            && event.altKey   === s.alt;
    }

    function navigate(path) {
        window.location.href = path;
    }

    function onKeyDown(event) {
        if (_aus) return;
        if (matches(event, _cfg.navHome)) {
            event.preventDefault();
            navigate('/');
        } else if (matches(event, _cfg.navKarte)) {
            event.preventDefault();
            navigate('/einsatz-karte');
        } else if (matches(event, _cfg.navMonitor)) {
            event.preventDefault();
            navigate('/einsatz-monitor');
        } else if (matches(event, _cfg.navStart)) {
            event.preventDefault();
            navigate('/einsatz-start');
        }
    }

    window.keyboardShortcuts = {
        configure: function (shortcuts) {
            const { aus, ...kuerzel } = shortcuts || {};
            _aus = aus === true;
            _cfg = Object.assign({}, _cfg, kuerzel);
        }
    };

    function init() {
        if (initialized) return;
        window.addEventListener('keydown', onKeyDown);
        initialized = true;
    }

    init();
})();
