document.addEventListener("DOMContentLoaded", runEnhancements);
document.body.addEventListener("htmx:afterSettle", runEnhancements);

function runEnhancements() {
    highlightMenu();
    // highlight all <pre><code>
    document.querySelectorAll('pre code').forEach(el => {
        addCopyPlugin();
        hljs.highlightElement(el);
    });

    // auto-trigger approve/deny
    const params = new URLSearchParams(window.location.search);
    const action = params.get("actionName");

    if (action === "approve") {
        document.getElementById("approve-button")?.click();
    } else if (action === "deny") {
        document.getElementById("deny-button")?.click();
    }
}



function highlightMenu() {
    const segments = window.location.pathname.split('/');
    const currentCtl = (segments[1] || '').toLowerCase();

    document.querySelectorAll('.menu a[href]').forEach(link => {
        const href = link.getAttribute('href');
        const linkCtl = (href.split('/')[1] || '').toLowerCase();
        const shouldHighlight = linkCtl === currentCtl;

        link.classList.toggle('border-primary', shouldHighlight);
        link.classList.toggle('font-extrabold', shouldHighlight);
    });
}