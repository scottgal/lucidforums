export default {
    encodeParameters: function (xhr, parameters, elt) {
        const ext = elt.getAttribute('hx-ext') || '';
        if (!ext.split(',').map(e => e.trim()).includes('dynamic-rowids')) {
            return null; // Use default behavior
        }

        const id = elt.dataset.id;
        const approve = elt.dataset.approve === 'true';
        const minimal = elt.dataset.minimal === 'true';
        const single = elt.dataset.single === 'true';

        const target = elt.dataset.target;
        const payload = { id, approve, minimal, single };

        if (approve && target) {
            const table = document.querySelector(target);
            if (table) {
                const rowIds = Array.from(table.querySelectorAll('tr[id^="row-"]'))
                    .map(row => row.id.replace('row-', ''));
                payload.rowIds = rowIds;
            }
        }

        // Merge payload into the parameters object
        Object.assign(parameters, payload);
        return null; // Return null to continue with default URL-encoded form encoding
    }
}