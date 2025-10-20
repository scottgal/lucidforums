import Swal from 'sweetalert2';

const Toast = Swal.mixin({
    toast: true,
    position: 'bottom-right',
    showConfirmButton: false,
    timerProgressBar: true,
    theme: 'dark',
    didOpen: (toast) => {
        toast.addEventListener('mouseenter', Swal.stopTimer);
        toast.addEventListener('mouseleave', Swal.resumeTimer);
    }
});

function isModalActive() {
    return Swal.isVisible() && !Swal.getPopup().classList.contains('swal2-toast');
}

let toastQueued = false;

export function showToast(message, duration = 3000, type = 'info') {
    if (isModalActive()) {
        if (toastQueued) return; // prevent multiple queues
        toastQueued = true;

        const observer = new MutationObserver((mutations, obs) => {
            if (!Swal.isVisible()) {
                Toast.fire({
                    icon: type,
                    title: message,
                    timer: duration
                });
                toastQueued = false;
                obs.disconnect();
            }
        });

        observer.observe(document.body, { childList: true, subtree: true });
        return;
    }

    Toast.fire({
        icon: type,
        title: message,
        timer: duration
    });
}


export function showHTMXToast(event) {
    const xhr = event?.detail?.xhr;
    let type = 'success';
    let message = xhr?.responseText || 'Done!';

    try {
        const data = xhr ? JSON.parse(xhr.responseText) : event.detail;

        if (data.toast) message = data.toast;
        if ('issuccess' in data) {
            type = data.issuccess === false ? 'error' : 'success';
        } else if (xhr?.status >= 400) {
            type = 'error';
        } else if (xhr?.status >= 300) {
            type = 'warning';
        }

    } catch {
        if (xhr?.status >= 400) type = 'error';
        else if (xhr?.status >= 300) type = 'warning';
    }

    showToast(message, 3000, type);
}