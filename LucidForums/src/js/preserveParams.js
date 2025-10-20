export default {
    onEvent: function (name, evt) {
        if (name !== 'htmx:configRequest') return;
        const el = evt.detail.elt;
        const excludeStr = el.getAttribute('preserve-params-exclude') || '';
        const exclude = excludeStr.split(',').map(s => s.trim());

        const currentParams = new URLSearchParams(window.location.search);
        const newParams = new URLSearchParams(evt.detail.path.split('?')[1] || '');

        currentParams.forEach((value, key) => {
            if (!exclude.includes(key) && !newParams.has(key)) {
                newParams.set(key, value);
            }
        });

        evt.detail.path = evt.detail.path.split('?')[0] + '?' + newParams.toString();

        return true;
    }
};