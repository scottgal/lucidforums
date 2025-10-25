// Real-time content translation updates via SignalR with HTMX OOB swaps
import * as signalR from '@microsoft/signalr';
import Swal from 'sweetalert2';

let translationConnection = null;
let activeTranslations = new Set(); // Track active translations for progress UI

export function initializeTranslationRealtime() {
    // Build connection to TranslationHub
    translationConnection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/translation')
        .withAutomaticReconnect()
        .build();

    // Listen for HTMX OOB swap events (new unified method)
    translationConnection.on('TranslationOOBSwap', (data) => {
        console.log('Translation OOB swap received:', data);
        handleOOBSwap(data);
    });

    // Listen for legacy content translation updates (backward compatibility)
    translationConnection.on('ContentTranslated', (data) => {
        console.log('Content translated (legacy):', data);
        handleContentTranslation(data);
    });

    // Listen for UI string translations (legacy)
    translationConnection.on('StringTranslated', (data) => {
        console.log('String translated (legacy):', data);
        handleStringTranslation(data);
    });

    // Listen for translation queued events
    translationConnection.on('ContentTranslationQueued', (data) => {
        console.log('Translation queued:', data);
        handleTranslationQueued(data);
    });

    // Listen for global translation progress
    translationConnection.on('TranslationProgress', (data) => {
        console.log('Translation progress:', data);
        handleTranslationProgress(data);
    });

    // Listen for translation job completion
    translationConnection.on('TranslationComplete', (data) => {
        console.log('Translation complete:', data);
        handleTranslationComplete(data);
    });

    // Start connection
    translationConnection.start()
        .then(() => {
            console.log('Translation SignalR connected');
        })
        .catch(err => {
            console.error('Translation SignalR connection error:', err);
        });
}

// Handle HTMX OOB swap - injects HTML fragment directly into the page
function handleOOBSwap(data) {
    const { ElementId, Html, LanguageCode } = data;

    // Get current user's selected language from cookie
    const currentLanguage = getCookie('preferred-language') || 'en';

    // Only swap if this translation matches the user's current language
    if (LanguageCode !== currentLanguage) {
        return;
    }

    // Create a temporary container
    const temp = document.createElement('div');
    temp.innerHTML = Html;

    // HTMX OOB swap: find the target element and replace it
    const targetElement = document.getElementById(ElementId);
    if (targetElement && temp.firstElementChild) {
        // Animate the swap
        targetElement.style.transition = 'opacity 0.2s';
        targetElement.style.opacity = '0.5';

        setTimeout(() => {
            targetElement.replaceWith(temp.firstElementChild);

            // Flash animation to indicate update
            const newElement = document.getElementById(ElementId);
            if (newElement) {
                newElement.style.opacity = '1';
                newElement.classList.add('translate-flash');
                setTimeout(() => {
                    newElement.classList.remove('translate-flash');
                }, 600);
            }

            // Remove from active translations
            activeTranslations.delete(ElementId);
            updateGlobalProgressUI();
        }, 200);
    }
}

// Show progress indicator for queued translations
function handleTranslationQueued(data) {
    const { contentType, contentId, fieldName, language } = data;
    const elementId = `content-${contentType}-${contentId}-${fieldName}`;

    // Get current user's selected language from cookie
    const currentLanguage = getCookie('preferred-language') || 'en';

    // Only show progress if this translation matches the user's current language
    if (language !== currentLanguage) {
        return;
    }

    // Find the element and show its progress indicator
    const element = document.getElementById(elementId);
    if (element) {
        const progressIndicator = element.querySelector('.translate-progress');
        if (progressIndicator) {
            progressIndicator.style.display = 'inline-block';
        }
        element.classList.add('translate-pending');

        // Track active translation
        activeTranslations.add(elementId);
        updateGlobalProgressUI();
    }
}

// Handle global translation progress (for batch operations)
function handleTranslationProgress(data) {
    const { Total, Completed, Percentage, CurrentKey } = data;

    // Show SweetAlert2 toast for progress if significant batch operation
    if (Total > 10 && activeTranslations.size > 5) {
        Swal.fire({
            toast: true,
            position: 'bottom-right',
            icon: 'info',
            title: `Translating content: ${Completed}/${Total} (${Math.round(Percentage)}%)`,
            showConfirmButton: false,
            timer: 2000,
            timerProgressBar: true
        });
    }
}

// Handle translation job completion
function handleTranslationComplete(data) {
    const { TranslatedCount } = data;

    // Clear all active translations
    activeTranslations.clear();
    updateGlobalProgressUI();

    // Show completion toast
    if (TranslatedCount > 0) {
        Swal.fire({
            toast: true,
            position: 'bottom-right',
            icon: 'success',
            title: `Translation complete: ${TranslatedCount} items translated`,
            showConfirmButton: false,
            timer: 3000,
            timerProgressBar: true
        });
    }
}

// Update global progress UI based on active translations
function updateGlobalProgressUI() {
    if (activeTranslations.size > 0) {
        // Show a subtle indicator that translations are in progress
        document.body.classList.add('translations-active');
    } else {
        document.body.classList.remove('translations-active');
    }
}

// Legacy handler for content translation updates (without OOB)
function handleContentTranslation(data) {
    const { contentType, contentId, fieldName, language, translatedText } = data;

    // Get current user's selected language from cookie
    const currentLanguage = getCookie('preferred-language') || 'en';

    // Only update if this translation matches the user's current language
    if (language !== currentLanguage) {
        return;
    }

    // Find elements that display this content
    const elementId = `content-${contentType}-${contentId}-${fieldName}`;
    const element = document.getElementById(elementId);

    if (element) {
        // Hide progress indicator
        const progressIndicator = element.querySelector('.translate-progress');
        if (progressIndicator) {
            progressIndicator.style.display = 'none';
        }
        element.classList.remove('translate-pending');

        // Update with translated text (encode for safety)
        const encoded = translatedText
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/\n/g, '<br/>');

        element.innerHTML = encoded + (progressIndicator ? progressIndicator.outerHTML : '');

        // Add a visual indicator that translation is complete
        element.classList.add('translate-flash');
        setTimeout(() => {
            element.classList.remove('translate-flash');
        }, 600);

        // Remove from active translations
        activeTranslations.delete(elementId);
        updateGlobalProgressUI();
    }
}

// Legacy handler for UI string translation updates
function handleStringTranslation(data) {
    const { Key, LanguageCode, TranslatedText } = data;

    // Get current user's selected language from cookie
    const currentLanguage = getCookie('preferred-language') || 'en';

    // Only update if this translation matches the user's current language
    if (LanguageCode !== currentLanguage) {
        return;
    }

    // Find all elements with this translation key
    const elements = document.querySelectorAll(`[data-translate-key="${CSS.escape(Key)}"]`);
    elements.forEach(element => {
        // Hide progress indicator
        const progressIndicator = element.querySelector('.translate-progress');
        if (progressIndicator) {
            progressIndicator.style.display = 'none';
        }

        // Update with translated text
        element.innerHTML = TranslatedText + (progressIndicator ? progressIndicator.outerHTML : '');

        // Add a visual indicator that translation is complete
        element.classList.add('translate-flash');
        setTimeout(() => {
            element.classList.remove('translate-flash');
        }, 600);
    });
}

function getCookie(name) {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop().split(';').shift();
    return null;
}

// Initialize on page load
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeTranslationRealtime);
} else {
    initializeTranslationRealtime();
}
