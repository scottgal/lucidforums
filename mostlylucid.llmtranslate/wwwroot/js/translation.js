/**
 * HTMX-Based Dynamic Translation System
 * Uses HTMX OOB (Out of Band) swaps for efficient content updates
 */

class TranslationManager {
    constructor() {
        this.currentLanguage = this.getCurrentLanguage();
        this.isTranslating = false;
    }

    getCurrentLanguage() {
        const value = `; ${document.cookie}`;
        const parts = value.split(`; preferred-language=`);
        if (parts.length === 2) {
            return parts.pop().split(';').shift();
        }
        return 'en';
    }

    /**
     * Collect all translation keys from the current page
     */
    collectTranslationKeys() {
        const elements = document.querySelectorAll('[data-translate-key]');
        return Array.from(elements).map(el => el.getAttribute('data-translate-key'));
    }

    /**
     * Switch language using HTMX OOB swaps
     * Server returns HTML with hx-swap-oob="innerHTML" for each translated element
     */
    async switchLanguageHtmx(languageCode) {
        if (this.isTranslating) return;

        try {
            this.isTranslating = true;
            this.showLoadingIndicator();

            // Collect all translation keys on the current page
            const keys = this.collectTranslationKeys();

            if (keys.length === 0) {
                console.log('No translatable content on this page');
                this.currentLanguage = languageCode;
                document.cookie = `preferred-language=${languageCode}; path=/; max-age=31536000; SameSite=Lax`;
                return;
            }

            // Build form data with all keys
            const formData = new FormData();
            keys.forEach(key => formData.append('keys', key));

            // Send HTMX request to get OOB swaps
            const response = await fetch(`/Language/Switch/${languageCode}`, {
                method: 'POST',
                body: formData
            });

            if (!response.ok) {
                throw new Error('Failed to switch language');
            }

            // Get HTML with OOB swaps
            const html = await response.text();

            // Create a temporary container
            const temp = document.createElement('div');
            temp.innerHTML = html;

            // Process all OOB swap elements
            temp.querySelectorAll('[hx-swap-oob]').forEach(element => {
                const targetId = element.id;
                const target = document.getElementById(targetId);

                if (target) {
                    // HTMX OOB swap: innerHTML means replace the content, not the element
                    target.innerHTML = element.innerHTML;

                    // Add subtle fade animation
                    target.style.transition = 'opacity 0.15s';
                    target.style.opacity = '0.8';
                    setTimeout(() => {
                        target.style.opacity = '1';
                    }, 75);
                }
            });

            this.currentLanguage = languageCode;
            console.log(`Language switched to ${languageCode} (${keys.length} elements updated)`);

        } catch (error) {
            console.error('Error switching language:', error);
        } finally {
            this.isTranslating = false;
            this.hideLoadingIndicator();
        }
    }

    async switchLanguage(languageCode) {
        if (languageCode === this.currentLanguage) {
            return; // Already in this language
        }

        // If switching to English, just reload (fastest option)
        if (languageCode === 'en') {
            document.cookie = `preferred-language=en; path=/; max-age=31536000; SameSite=Lax`;
            window.location.reload();
            return;
        }

        // Use HTMX OOB swap approach for efficient translation
        await this.switchLanguageHtmx(languageCode);
    }

    showLoadingIndicator() {
        // Create or show a loading indicator
        let indicator = document.getElementById('translation-loading');
        if (!indicator) {
            indicator = document.createElement('div');
            indicator.id = 'translation-loading';
            indicator.className = 'toast toast-center';
            indicator.innerHTML = `
                <div class="alert alert-info">
                    <span class="loading loading-spinner loading-sm"></span>
                    <span>Loading translations...</span>
                </div>
            `;
            document.body.appendChild(indicator);
        } else {
            indicator.style.display = 'block';
        }
    }

    hideLoadingIndicator() {
        const indicator = document.getElementById('translation-loading');
        if (indicator) {
            setTimeout(() => {
                indicator.style.display = 'none';
            }, 300);
        }
    }

    initialize() {
        // Set initial language display
        const langDisplay = document.getElementById('current-lang');
        if (langDisplay) {
            langDisplay.textContent = this.currentLanguage.toUpperCase();
        }

        // If a non-English language is already selected (via cookie or prior choice),
        // immediately kick off the translation pass so first render matches selection.
        // Use the HTMX/OOB path directly to avoid reloads and to populate missing strings.
        const desiredLang = this.currentLanguage || 'en';
        if (desiredLang && desiredLang.toLowerCase() !== 'en') {
            // Defer to end of tick to ensure DOM is fully ready
            setTimeout(() => {
                // Call the HTMX path directly so we don't early-return due to equality check
                this.switchLanguageHtmx(desiredLang);
            }, 0);
        }

        console.log(`Translation system initialized (current language: ${this.currentLanguage})`);
    }
}

// Create global instance
window.translationManager = new TranslationManager();

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        window.translationManager.initialize();
    });
} else {
    window.translationManager.initialize();
}

// Global function for language switching (called from onclick)
window.setLanguage = function(languageCode) {
    window.translationManager.switchLanguage(languageCode);
};
