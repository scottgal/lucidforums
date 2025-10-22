// ======================
// 1) VENDOR IMPORTS
// ======================
import htmx from 'htmx.org';
import Alpine from 'alpinejs';
import Swal from 'sweetalert2';
import flatpickr from 'flatpickr';
import hljs from 'highlight.js/lib/core';
import jsonLang from 'highlight.js/lib/languages/json';
import xmlLang from 'highlight.js/lib/languages/xml';

window.htmx = htmx;
window.Swal = Swal;
window.flatpickr = flatpickr;
window.hljs = hljs;
// ======================
// 2) VENDOR CSS
// ======================
import 'flatpickr/dist/flatpickr.min.css';
import 'flatpickr/dist/themes/dark.css';
import 'highlight.js/styles/atom-one-dark-reasonable.min.css';

// ======================
// 3) LOCAL IMPORTS
// ======================
import { registerSweetAlertHxIndicator, showAlert } from './hx-sweetalert-indicator.js';
import { typeahead } from './typeahead';
import  './highlight-copy.js';
import { registerThemeSwitcher } from './theme-switcher.js';
import { autoUpdateController } from './autoUpdate.js';
import { showToast, showHTMXToast } from './toast';
import './htmx-events';
import { queryParamClearer, queryParamToggler } from './param-utils';
import {runEnhancements} from './auto-actions.js';
import "./download-modal"
import "./toggle-persist.js"
import utcdateDirective from './utcdate.js';
import dynamicRowIds from "./dynamicRowIds";
import preserveParams from './preserveParams.js';
import './confirm'
import './editor.js'
// Page-specific behaviors
import './pages/admin-ai-settings.js'
import './pages/admin-ai-test.js'
import './pages/setup.js'
import * as signalR from '@microsoft/signalr';
// ======================
// 4) INITIAL SETUP
// ======================
// Highlight.js languages
hljs.registerLanguage('json', jsonLang);
hljs.registerLanguage('html', xmlLang);

// HTMX extensions
htmx.defineExtension("dynamic-rowids", dynamicRowIds);
htmx.defineExtension('preserve-params', preserveParams);

// ======================
// 5) ALPINE INITIALIZATION
// ======================
window.Alpine = Alpine;

// ======================
// 8) GLOBAL EXPORTS (defined early to be available during Alpine init)
// ======================
window.typeahead = typeahead;
window.autoUpdateController = autoUpdateController;
window.showToast = showToast;
window.showHTMXToast = showHTMXToast;
window.queryParamClearer = queryParamClearer;
window.queryParamToggler = queryParamToggler;
window.showAlert = showAlert;

document.body.addEventListener("showToast", showHTMXToast);

// Register directives and components
document.addEventListener('alpine:init', () => {
    Alpine.directive('utcdate', utcdateDirective);
    Alpine.data('autoUpdate', function() {
        return autoUpdateController(
            this.$el.dataset.endpointId,
            this.$el.dataset.actionUrl,
            parseInt(this.$el.dataset.interval || '30', 10)
        );
    });
});

// Register theme switcher BEFORE Alpine.start to ensure x-data="themeSwitcher()" is available
registerThemeSwitcher(Alpine);
Alpine.start();

// HTMX content swaps
htmx.onLoad(function(content) {

    Alpine.initTree(content); // Initialize Alpine on new content
});

// ======================
// 9) INITIALIZATION
// ======================
registerSweetAlertHxIndicator();

// TranslationHub: live per-string updates via SignalR
async function initTranslationHub() {
    // Simple style injection for element highlight and progress chip
    const styleId = 'lf-translation-style';
    if (!document.getElementById(styleId)) {
        const st = document.createElement('style');
        st.id = styleId;
        st.textContent = `
            .lf-translation-updating { outline: 2px solid var(--fallback-p, #60a5fa); outline-offset: 2px; transition: outline-color .2s, opacity .2s; }
            .lf-translation-progress { position: fixed; right: 12px; bottom: 12px; z-index: 2147483646; background: rgba(0,0,0,.75); color: #fff; padding: 8px 10px; border-radius: 8px; font-size: 12px; box-shadow: 0 6px 18px rgba(0,0,0,.3); max-width: 60vw; }
            .lf-translation-progress .bar { height: 4px; background: rgba(255,255,255,.2); border-radius: 999px; overflow: hidden; margin-top: 6px; }
            .lf-translation-progress .bar > span { display: block; height: 100%; background: #60d394; width: 0%; transition: width .2s ease; }
            .lf-translation-progress .row { display: flex; align-items: center; gap: 8px; }
            .lf-translation-progress .key { color: #c7d2fe; word-break: break-all; opacity: .9; }
        `;
        document.head.appendChild(st);
    }

    function ensureProgressUI() {
        let el = document.getElementById('lf-translation-progress');
        if (!el) {
            el = document.createElement('div');
            el.id = 'lf-translation-progress';
            el.className = 'lf-translation-progress';
            el.innerHTML = `
                <div class="row"><strong>Translating…</strong><span class="pct">0%</span><span class="cnt"></span></div>
                <div class="key"></div>
                <div class="bar"><span></span></div>
            `;
            document.body.appendChild(el);
        }
        return el;
    }

    function updateProgressUI(total, completed, currentKey) {
        const ui = ensureProgressUI();
        const pct = total > 0 ? Math.round((completed / total) * 100) : 0;
        ui.querySelector('.pct').textContent = `${pct}%`;
        ui.querySelector('.cnt').textContent = total > 0 ? `(${completed}/${total})` : '';
        ui.querySelector('.key').textContent = currentKey ? `Updating: ${currentKey}` : '';
        ui.querySelector('.bar > span').style.width = `${pct}%`;
        ui.style.display = 'block';
    }

    function completeProgressUI() {
        const ui = document.getElementById('lf-translation-progress');
        if (ui) {
            ui.querySelector('.pct').textContent = `100%`;
            ui.querySelector('.bar > span').style.width = `100%`;
            ui.querySelector('.key').textContent = 'Done';
            setTimeout(() => { ui.style.display = 'none'; }, 1200);
        }
    }

    try {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/translation')
            .withAutomaticReconnect()
            .build();

        connection.on('StringTranslated', (payload) => {
            const key = payload?.Key || payload?.key;
            const translatedText = payload?.TranslatedText || payload?.translatedText;
            if (!key) return;
            const el = document.querySelector(`[data-translate-key="${CSS.escape(key)}"]`);
            if (!el) return;
            // Highlight element being swapped
            el.classList.add('lf-translation-updating');
            el.innerHTML = translatedText ?? '';
            // Subtle fade to indicate update
            el.style.transition = 'opacity 0.15s';
            el.style.opacity = '0.85';
            setTimeout(() => { el.style.opacity = '1'; el.classList.remove('lf-translation-updating'); }, 200);
        });

        connection.on('TranslationProgress', (payload) => {
            const total = payload?.Total ?? payload?.total ?? 0;
            const completed = payload?.Completed ?? payload?.completed ?? 0;
            const currentKey = payload?.CurrentKey ?? payload?.currentKey ?? '';
            updateProgressUI(total, completed, currentKey);
        });

        connection.on('TranslationComplete', () => {
            completeProgressUI();
        });

        await connection.start();
    } catch (e) {
        console.warn('TranslationHub connection failed', e);
    }
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => initTranslationHub());
} else {
    initTranslationHub();
}