// Real-time content translation updates via SignalR
import * as signalR from '@microsoft/signalr';

let translationConnection = null;

export function initializeTranslationRealtime() {
    // Build connection to TranslationHub
    translationConnection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/translation')
        .withAutomaticReconnect()
        .build();

    // Listen for content translation updates
    translationConnection.on('ContentTranslated', (data) => {
        console.log('Content translated:', data);
        handleContentTranslation(data);
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

function handleContentTranslation(data) {
    const { contentType, contentId, fieldName, language, translatedText } = data;

    // Get current user's selected language from cookie
    const currentLanguage = getCookie('Language') || 'en';

    // Only update if this translation matches the user's current language
    if (language !== currentLanguage) {
        return;
    }

    // Find elements that display this content
    // For messages, look for message container with data-message-id
    if (contentType === 'Message' && fieldName === 'Content') {
        const messageElement = document.querySelector(`[data-message-id="${contentId}"]`);
        if (messageElement) {
            // Find the content area within the message
            const contentElement = messageElement.querySelector('.message-content');
            if (contentElement) {
                // Update with translated text
                contentElement.textContent = translatedText;

                // Add a visual indicator that translation is complete
                messageElement.classList.add('translation-updated');
                setTimeout(() => {
                    messageElement.classList.remove('translation-updated');
                }, 2000);
            }
        }
    }
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
