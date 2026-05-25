window.hilfeDrawer = {
    scrollToSection(id) {
        const body = document.getElementById('hilfeDrawerBody');
        const el = document.getElementById(id);
        if (body && el) {
            const bodyRect = body.getBoundingClientRect();
            const elementRect = el.getBoundingClientRect();
            const targetTop = Math.max(0, body.scrollTop + (elementRect.top - bodyRect.top) - 8);
            body.scrollTo({ top: targetTop, behavior: 'smooth' });
        }
    }
};
