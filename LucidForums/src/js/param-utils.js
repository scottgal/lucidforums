export function queryParamClearer({ path = window.location.pathname }) {
    return {
        clearParam(e) {
            const el = e.target.closest('[x-param],[x-all]');
            if (!el) return;

            const url = new URL(window.location.href);

            // Get excluded params (case-insensitive)
            const excludedParams = (el.getAttribute('x-exclude') || '')
                .split(',')
                .map(p => p.trim().toLowerCase())
                .filter(Boolean);

            if (el.hasAttribute('x-all')) {
                // Delete every param except excluded ones
                Array.from(url.searchParams.keys())
                    .forEach(key => {
                        if (!excludedParams.includes(key.toLowerCase())) {
                            url.searchParams.delete(key);
                        }
                    });
            } else {
                // Delete only the named params, case-insensitive, except excluded ones
                const targets = (el.getAttribute('x-param') || '')
                    .split(',')
                    .map(p => p.trim().toLowerCase())
                    .filter(Boolean)
                    .filter(target => !excludedParams.includes(target)); // Exclude any targets that are in excludedParams

                // For each existing key, if its lowercase matches one of the targets, delete it
                Array.from(url.searchParams.keys())
                    .forEach(actualKey => {
                        if (targets.includes(actualKey.toLowerCase()) &&
                            !excludedParams.includes(actualKey.toLowerCase())) {
                            url.searchParams.delete(actualKey);
                        }
                    });
            }

            const qs = url.searchParams.toString();
            const newUrl = path + (qs ? `?${qs}` : '');

            showAlert(newUrl);
            htmx.ajax('GET', newUrl, {
                target: el.dataset.target || el.getAttribute('hx-target') || 'body',
                swap: 'innerHTML',
                pushUrl: true
            });
        }
    };
}

export function queryParamToggler({ param, target = null, path = window.location.pathname }) {
    return {
        param,
        target,
        path,
        toggle(event) {
            const input = event.target;
            const value = input.dataset.value ?? input.value;
            const param = input.dataset.param || this.param;
            const targetSelector = input.dataset.target || this.target || input.getAttribute('hx-target') || 'body';
            const checked = input.checked;
            const url = new URL(window.location.href);

            if (checked) {
                url.searchParams.set(param, value);
            } else {
                url.searchParams.delete(param);
            }

            const query = url.searchParams.toString();
            const newUrl = this.path + (query ? `?${query}` : '');
            showAlert(newUrl);
            htmx.ajax('GET', newUrl, {
                target: targetSelector,
                swap: 'innerHTML',
                pushUrl: true
            });
        }
    };
}