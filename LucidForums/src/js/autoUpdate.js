export function autoUpdateController(endpointId, actionUrl, initialInterval = 30) {
    const keyPrefix = `autoUpdate:${actionUrl}`;
    const enabledKey = `${keyPrefix}:enabled`;

    return {
        autoUpdate: false,
        interval: initialInterval,

        toggleAutoUpdate() {
            const el = document.getElementById(endpointId);
            if (!el) return;

            const url = new URL(window.location.href);
            const query = url.searchParams.toString();
            const fullUrl = query ? `${actionUrl}?${query}` : actionUrl;

            const wasPreviouslyEnabled = localStorage.getItem(enabledKey) === 'true';

            if (this.autoUpdate) {
                el.setAttribute('hx-trigger', `every ${this.interval}s`);
                el.setAttribute('hx-swap', 'innerHTML');
                el.setAttribute('hx-get', fullUrl);
                el.setAttribute('hx-headers', JSON.stringify({ AutoPoll: 'auto' }));

                localStorage.setItem(enabledKey, 'true');

                htmx.process(el); // rebind with updated attributes
                
                if (!wasPreviouslyEnabled) {
                    htmx.ajax('GET', fullUrl, {
                        target: el,
                        swap: 'innerHTML',
                        headers: {AutoPoll: 'auto'}
                    });
                }
            } else {
                el.removeAttribute('hx-trigger');
                el.removeAttribute('hx-get');
                el.removeAttribute('hx-swap');
                el.removeAttribute('hx-headers');

                localStorage.removeItem(enabledKey);
                htmx.process(el);
            }
        },

        init() {
            this.autoUpdate = localStorage.getItem(enabledKey) === 'true';
            this.toggleAutoUpdate();
        }
    };
}