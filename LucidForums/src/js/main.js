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