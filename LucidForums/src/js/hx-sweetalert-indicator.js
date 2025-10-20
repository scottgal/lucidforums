import Swal from 'sweetalert2';

const SWEETALERT_PATH_KEY = 'swal-active-path'; // Stores the path of the current SweetAlert
const SWEETALERT_HISTORY_RESTORED_KEY = 'swal-just-restored'; // Flag for navigation from browser history
const SWEETALERT_TIMEOUT_MS = 20000; // Timeout for automatically closing the loader

let swalTimeoutHandle = null;

export function registerSweetAlertHxIndicator() {
    document.body.addEventListener('htmx:beforeRequest', function (evt) {
        const trigger = evt.detail.elt;

        const indicatorAttrSource = getIndicatorSource(trigger);
        if (!indicatorAttrSource) return;

        // ✅ If this is a pageSize-triggered request, use our custom path
        let path = getRequestPath(evt.detail);
        

        if (!path) return;
        evt.detail.indicator = null; 
        showAlert(path);
    });
    }

    export function showAlert(path)
    {
        const currentPath = sessionStorage.getItem(SWEETALERT_PATH_KEY);

        // Show SweetAlert only if the current request path differs from the previous one
        if (currentPath !== path) {
            closeSweetAlertLoader();
            sessionStorage.setItem(SWEETALERT_PATH_KEY, path);
    

            Swal.fire({
                title: 'Loading...',
                allowOutsideClick: false,
                allowEscapeKey: false,
                showConfirmButton: false,
                theme: 'dark',
                didOpen: () => {
                    // Cancel immediately if restored from browser history
                    if (sessionStorage.getItem(SWEETALERT_HISTORY_RESTORED_KEY) === 'true') {
                        sessionStorage.removeItem(SWEETALERT_HISTORY_RESTORED_KEY);
                        Swal.close();
                        return;
                    }

                    Swal.showLoading();
                    document.dispatchEvent(new CustomEvent('sweetalert:opened'));

                    // Set timeout to auto-close if something hangs
                    clearTimeout(swalTimeoutHandle);
                    swalTimeoutHandle = setTimeout(() => {
                        if (Swal.isVisible()) {
                            console.warn('SweetAlert loading modal closed after timeout.');
                            closeSweetAlertLoader();
                        }
                    }, SWEETALERT_TIMEOUT_MS);
                },
                didClose: () => {
                    document.dispatchEvent(new CustomEvent('sweetalert:closed'));
                    sessionStorage.removeItem(SWEETALERT_PATH_KEY);
                    clearTimeout(swalTimeoutHandle);
                    swalTimeoutHandle = null;
                }
            });
        }
    }
    
    document.body.addEventListener('htmx:afterRequest', maybeClose);
    document.body.addEventListener('htmx:responseError', maybeClose);
    document.body.addEventListener('sweetalert:close', closeSweetAlertLoader);

    // Set a flag so we can suppress the loader immediately if navigating via browser history
    document.body.addEventListener('htmx:historyRestore', () => {
        sessionStorage.setItem(SWEETALERT_HISTORY_RESTORED_KEY, 'true');
    });

    window.addEventListener('popstate', () => {
        sessionStorage.setItem(SWEETALERT_HISTORY_RESTORED_KEY, 'true');
    });


// Returns the closest element with an indicator attribute
function getIndicatorSource(el) {
    return el.closest('[hx-indicator], [data-hx-indicator]');
}

// Determines the request path, including query string if appropriate
function getRequestPath(detail) {
    const responsePath = typeof(detail?.pathInfo?.finalRequestPath) === 'string' ?
        detail.pathInfo.finalRequestPath :
        typeof detail?.pathInfo?.responsePath === 'string'
            ? detail.pathInfo.responsePath
            : (typeof detail?.pathInfo?.path === 'string'
                    ? detail.pathInfo.path
                    : (typeof detail?.path === 'string' ? detail.path : '')
            );

    const elt = detail.elt;

    // If not a form and has an hx-indicator, use the raw path
    if (elt.hasAttribute("hx-indicator") && elt.tagName !== "FORM") {
        return responsePath;
    }

    const isGet = (detail.verb ?? '').toUpperCase() === 'GET';
    const form = elt.closest('form');

    // Append query params for GET form submissions
    if (isGet && form) {
        const params = new URLSearchParams();

        for (const el of form.elements) {
            if (!el.name || el.disabled) continue;

            const type = el.type;
            if ((type === 'checkbox' || type === 'radio') && !el.checked) continue;
            if (type === 'submit') continue;

            params.append(el.name, el.value);
        }

        const queryString = params.toString();
        return queryString ? `${responsePath}?${queryString}` : responsePath;
    }

    return responsePath;
}

// Closes the SweetAlert loader if the path matches
function maybeClose(evt) {
    const activePath = sessionStorage.getItem(SWEETALERT_PATH_KEY);
    const path = getRequestPath(evt.detail);

    if (activePath && path && activePath === path) {
        closeSweetAlertLoader();
    }
}

// Close and clean up SweetAlert loader state
function closeSweetAlertLoader() {
    if (Swal.getPopup()) {
        Swal.close();
        document.dispatchEvent(new CustomEvent('sweetalert:closed'));
        sessionStorage.removeItem(SWEETALERT_PATH_KEY);
        clearTimeout(swalTimeoutHandle);
        swalTimeoutHandle = null;
    }
}