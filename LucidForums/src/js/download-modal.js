window.initDownloadModal = (buttonId) => {
    const button = document.getElementById(buttonId);
    if (!button) return;

    button.addEventListener('click', () => {
        const form = button.closest('form');
        if (!form) return;

        // Remove previously added dynamic inputs
        form.querySelectorAll('input[data-added-from-search]').forEach(el => el.remove());

        // Add current URL search params as hidden fields
        const params = new URLSearchParams(window.location.search);
        for (const [key, value] of params.entries()) {
            const input = document.createElement('input');
            input.type = 'hidden';
            input.name = key;
            input.value = value;
            input.setAttribute('data-added-from-search', 'true'); // mark for cleanup
            form.appendChild(input);
        }

        // Show SweetAlert loading modal
        Swal.fire({
            title: 'Preparing file...',
            allowOutsideClick: false,
            allowEscapeKey: false,
            showConfirmButton: false,
            theme: 'dark',
            didOpen: () => {
                Swal.showLoading();
            }
        });

        // Watch for downloadStarted cookie to close modal
        const interval = setInterval(() => {
            if (document.cookie.includes('downloadStarted=true')) {
                clearInterval(interval);
                Swal.close();
                document.cookie = 'downloadStarted=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;';
            }
        }, 500);
    });
};