// Alpine directive to persist toggle (checkbox) state to localStorage
// Usage: add x-persist-toggle on an <input type="checkbox"> element.
// Optionally pass a key: x-persist-toggle="'my-key'"
// If no key is provided, it will use the element's name or id.

(function(){
  const STORAGE_PREFIX = 'toggle:';
  const getKey = (el, expression) => {
    if (expression && typeof expression === 'string' && expression.trim().length > 0) {
      return STORAGE_PREFIX + expression.replace(/^['\"]|['\"]$/g, '');
    }
    if (el.name && el.name.length > 0) return STORAGE_PREFIX + el.name;
    if (el.id && el.id.length > 0) return STORAGE_PREFIX + el.id;
    // Fallback unique key based on DOM path
    return STORAGE_PREFIX + (el.closest('[data-page-key]')?.getAttribute('data-page-key') || window.location.pathname) + ':' + Math.random().toString(36).slice(2);
  };

  document.addEventListener('alpine:init', () => {
    const Alpine = window.Alpine;
    Alpine.directive('persist-toggle', (el, { expression }, { evaluateLater }) => {
      if (!(el instanceof HTMLInputElement) || el.type !== 'checkbox') return;

      const storageKey = getKey(el, expression);
      try {
        const saved = localStorage.getItem(storageKey);
        if (saved !== null) {
          el.checked = saved === 'true';
        }
      } catch (e) {
        // localStorage might be blocked; ignore
      }

      const persist = () => {
        try {
          localStorage.setItem(storageKey, el.checked ? 'true' : 'false');
        } catch (e) {
          // ignore storage errors
        }
      };

      el.addEventListener('change', persist);

      // If the element is removed, remove listener (optional cleanup)
      const observer = new MutationObserver(() => {
        if (!document.body.contains(el)) {
          el.removeEventListener('change', persist);
          observer.disconnect();
        }
      });
      observer.observe(document.body, { childList: true, subtree: true });
    });
  });
})();
