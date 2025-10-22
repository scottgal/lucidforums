/**
 * LLM Translation System - Complete Bundle
 * Includes SignalR connection and all translation functionality
 * No external dependencies required
 */

// Include SignalR client library inline (for standalone operation)
// In production, you should include the actual SignalR client library here
// or load it from CDN via the translation-scripts tag helper

(function(window) {
    'use strict';

    /**
     * Translation Manager - Handles HTMX-based translation switching
     */
    class TranslationManager {
        constructor(options = {}) {
            this.currentLanguage = this.getCurrentLanguage();
            this.isTranslating = false;
            this.debug = options.debug || false;
            this.signalRHub = options.signalRHub || '/hubs/translation';
            this.enableNotifications = options.enableNotifications !== false;
            this.signalRConnection = null;

            if (this.debug) {
                console.log('[Translation] Initializing with options:', options);
            }
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
         */
        async switchLanguageHtmx(languageCode) {
            if (this.isTranslating) {
                if (this.debug) console.log('[Translation] Already translating, ignoring request');
                return;
            }

            try {
                this.isTranslating = true;
                this.showLoadingIndicator();

                const keys = this.collectTranslationKeys();

                if (keys.length === 0) {
                    if (this.debug) console.log('[Translation] No translatable content on this page');
                    this.currentLanguage = languageCode;
                    document.cookie = `preferred-language=${languageCode}; path=/; max-age=31536000; SameSite=Lax`;
                    this.updateCurrentLanguageDisplay(languageCode);
                    return;
                }

                const formData = new FormData();
                keys.forEach(key => formData.append('keys', key));

                const response = await fetch(`/Language/Switch/${languageCode}`, {
                    method: 'POST',
                    body: formData
                });

                if (!response.ok) {
                    throw new Error(`Failed to switch language: ${response.statusText}`);
                }

                const html = await response.text();
                const temp = document.createElement('div');
                temp.innerHTML = html;

                let updatedCount = 0;
                temp.querySelectorAll('[hx-swap-oob]').forEach(element => {
                    const targetId = element.id;
                    const target = document.getElementById(targetId);

                    if (target) {
                        target.innerHTML = element.innerHTML;
                        this.animateTranslationUpdate(target);
                        updatedCount++;
                    }
                });

                this.currentLanguage = languageCode;
                this.updateCurrentLanguageDisplay(languageCode);

                if (this.debug) {
                    console.log(`[Translation] Language switched to ${languageCode} (${updatedCount}/${keys.length} elements updated)`);
                }

                if (this.enableNotifications) {
                    this.showNotification(`Language changed to ${this.getLanguageName(languageCode)}`, 'success');
                }

            } catch (error) {
                console.error('[Translation] Error switching language:', error);
                if (this.enableNotifications) {
                    this.showNotification('Failed to switch language', 'error');
                }
            } finally {
                this.isTranslating = false;
                this.hideLoadingIndicator();
            }
        }

        async switchLanguage(languageCode) {
            if (languageCode === this.currentLanguage) {
                if (this.debug) console.log('[Translation] Already in this language');
                return;
            }

            if (languageCode === 'en') {
                document.cookie = `preferred-language=en; path=/; max-age=31536000; SameSite=Lax`;
                window.location.reload();
                return;
            }

            await this.switchLanguageHtmx(languageCode);
        }

        animateTranslationUpdate(element) {
            element.style.transition = 'background-color 0.5s ease';
            element.style.backgroundColor = '#ffffcc';
            setTimeout(() => {
                element.style.backgroundColor = '';
                setTimeout(() => {
                    element.style.transition = '';
                }, 500);
            }, 500);
        }

        updateCurrentLanguageDisplay(langCode) {
            const displays = document.querySelectorAll('#current-lang, [data-current-lang]');
            displays.forEach(display => {
                display.textContent = langCode.toUpperInvariant();
            });
        }

        showLoadingIndicator() {
            const indicators = document.querySelectorAll('#translation-loading-indicator, [data-translation-loading]');
            indicators.forEach(indicator => indicator.classList.remove('d-none'));

            let indicator = document.getElementById('translation-loading');
            if (!indicator) {
                indicator = document.createElement('div');
                indicator.id = 'translation-loading';
                indicator.className = 'toast-container position-fixed top-0 end-0 p-3';
                indicator.innerHTML = `
                    <div class="toast show" role="alert">
                        <div class="toast-body d-flex align-items-center gap-2">
                            <div class="spinner-border spinner-border-sm" role="status">
                                <span class="visually-hidden">Loading...</span>
                            </div>
                            <span>Loading translations...</span>
                        </div>
                    </div>
                `;
                document.body.appendChild(indicator);
            } else {
                indicator.style.display = 'block';
            }
        }

        hideLoadingIndicator() {
            const indicators = document.querySelectorAll('#translation-loading-indicator, [data-translation-loading]');
            indicators.forEach(indicator => indicator.classList.add('d-none'));

            const indicator = document.getElementById('translation-loading');
            if (indicator) {
                setTimeout(() => {
                    indicator.style.display = 'none';
                }, 300);
            }
        }

        showNotification(message, type = 'info') {
            const container = document.getElementById('translation-notifications') || this.createNotificationContainer();
            const notification = document.createElement('div');
            notification.className = `alert alert-${type === 'error' ? 'danger' : type === 'success' ? 'success' : 'info'} alert-dismissible fade show`;
            notification.innerHTML = `
                ${message}
                <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
            `;
            container.appendChild(notification);

            setTimeout(() => {
                notification.classList.remove('show');
                setTimeout(() => notification.remove(), 150);
            }, 3000);
        }

        createNotificationContainer() {
            const container = document.createElement('div');
            container.id = 'translation-notifications';
            container.className = 'position-fixed top-0 end-0 p-3';
            container.style.zIndex = '1060';
            document.body.appendChild(container);
            return container;
        }

        getLanguageName(code) {
            const names = {
                'en': 'English', 'es': 'Español', 'fr': 'Français', 'de': 'Deutsch',
                'it': 'Italiano', 'pt': 'Português', 'ru': 'Русский', 'ja': '日本語',
                'ko': '한국어', 'zh': '中文', 'ar': 'العربية', 'hi': 'हिन्दी'
            };
            return names[code.toLowerCase()] || code.toUpperInvariant();
        }

        /**
         * Initialize SignalR connection for real-time updates
         */
        initializeSignalR() {
            if (typeof signalR === 'undefined') {
                if (this.debug) console.warn('[Translation] SignalR not available, skipping real-time updates');
                return;
            }

            try {
                this.signalRConnection = new signalR.HubConnectionBuilder()
                    .withUrl(this.signalRHub)
                    .withAutomaticReconnect()
                    .build();

                this.signalRConnection.on('StringTranslated', (data) => {
                    if (this.debug) console.log('[Translation] String translated:', data);

                    const elementId = `t-${this.simpleHash(data.key)}`;
                    const element = document.getElementById(elementId);

                    if (element && data.languageCode === this.currentLanguage) {
                        element.innerHTML = data.translatedText;
                        this.animateTranslationUpdate(element);
                    }
                });

                this.signalRConnection.on('TranslationProgress', (data) => {
                    if (this.debug) console.log('[Translation] Progress:', data);
                    this.updateProgressDisplay(data);
                });

                this.signalRConnection.on('TranslationComplete', (data) => {
                    if (this.debug) console.log('[Translation] Complete:', data);
                    this.hideProgressDisplay();

                    if (this.enableNotifications) {
                        this.showNotification(`${data.translatedCount} translations completed`, 'success');
                    }
                });

                this.signalRConnection.start()
                    .then(() => {
                        if (this.debug) console.log('[Translation] SignalR connected');
                    })
                    .catch(err => {
                        console.error('[Translation] SignalR connection error:', err);
                    });

            } catch (error) {
                console.error('[Translation] Error initializing SignalR:', error);
            }
        }

        updateProgressDisplay(data) {
            const progressBar = document.getElementById('translation-progress-bar');
            const progressText = document.getElementById('translation-progress-text');
            const progressContainer = document.getElementById('translation-progress');

            if (progressBar) {
                progressBar.style.width = `${data.percentage}%`;
            }

            if (progressText) {
                progressText.textContent = `${data.completed} / ${data.total} (${Math.round(data.percentage)}%)`;
            }

            if (progressContainer && data.total > 0) {
                progressContainer.classList.remove('d-none');
            }
        }

        hideProgressDisplay() {
            const progressContainer = document.getElementById('translation-progress');
            if (progressContainer) {
                setTimeout(() => progressContainer.classList.add('d-none'), 2000);
            }
        }

        simpleHash(str) {
            let hash = 0;
            for (let i = 0; i < str.length; i++) {
                const char = str.charCodeAt(i);
                hash = ((hash << 5) - hash) + char;
                hash |= 0;
            }
            return Math.abs(hash).toString(16).substring(0, 16).padStart(16, '0');
        }

        initialize() {
            this.updateCurrentLanguageDisplay(this.currentLanguage);

            const desiredLang = this.currentLanguage || 'en';
            if (desiredLang && desiredLang.toLowerCase() !== 'en') {
                setTimeout(() => {
                    this.switchLanguageHtmx(desiredLang);
                }, 100);
            }

            this.initializeSignalR();

            if (this.debug) {
                console.log(`[Translation] System initialized (language: ${this.currentLanguage})`);
            }
        }
    }

    // Global API
    window.TranslationManager = TranslationManager;

    // Auto-initialize with config from tag helper or defaults
    const config = window.translationConfig || {};
    window.translationManager = new TranslationManager({
        debug: config.debug || false,
        signalRHub: config.signalRHub || '/hubs/translation',
        enableNotifications: config.enableNotifications !== false
    });

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            window.translationManager.initialize();
        });
    } else {
        window.translationManager.initialize();
    }

    // Global function for language switching (backward compatibility)
    window.setLanguage = function(languageCode) {
        window.translationManager.switchLanguage(languageCode);
    };

})(window);
