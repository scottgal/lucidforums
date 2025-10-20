function isHtml(str) {
    const doc = new DOMParser().parseFromString(str, 'text/html');
    const parsed = doc.body;
    return parsed.innerHTML !== parsed.textContent;
}


// Intercept all htmx:confirm events and replace with SweetAlert
window.addEventListener('htmx:confirm', (e) => {
    const message = e.detail.question;
    if (!message) return;

    e.preventDefault(); // stop HTMX's default confirm

    Swal.fire({
        title: 'Please confirm',
        icon: 'warning',
        showCancelButton: true,
        theme: 'dark',
        confirmButtonText: 'Yes',
        cancelButtonText: 'Cancel',
        ...(isHtml(message) ? { html: message } : { text: message })
    }).then(({ isConfirmed }) => {
        if (isConfirmed) {
            e.detail.issueRequest(true);
        }
    });
});

// ALSO handle non-HTMX links with hx-confirm manually
document.addEventListener('click', function (e) {
    const el = e.target.closest('a[hx-confirm]');
    if (!el) return;

    const hasHtmxTrigger =
        el.hasAttribute('hx-get') ||
        el.hasAttribute('hx-post') ||
        el.hasAttribute('hx-put') ||
        el.hasAttribute('hx-delete');

    if (hasHtmxTrigger) return;

    e.preventDefault();

    const message = el.getAttribute('hx-confirm');
    if (!message) return;

    Swal.fire({
        title: 'Please confirm',
        icon: 'warning',
        theme: 'dark',
        showCancelButton: true,
        confirmButtonText: 'Yes',
        cancelButtonText: 'Cancel',
        ...(isHtml(message) ? { html: message } : { text: message })
    }).then(({ isConfirmed }) => {
        if (isConfirmed) {
            window.location.href = el.href;
        }
    });
});