// Editor page initialization: EasyMDE setup + SSE translation stream
// Depends on window.htmx (provided by our bundle) and EasyMDE (CDN on the page)

(function(){
  let currentES = null;
  let debounceTimer = null;

  function debounce(fn, delay) {
    return function() {
      clearTimeout(debounceTimer);
      debounceTimer = setTimeout(fn, delay);
    }
  }

  function saveContentToDisk(content, slug, language) {
    try {
      const filename = (slug || 'untitled') + (language === 'en' ? '.md' : `.${language}.md`);
      const blob = new Blob([content], { type: 'text/markdown;charset=utf-8;' });
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
    } catch (e) {
      console.error('Save failed:', e);
    }
  }

  function startTranslationStream() {
    const auto = document.getElementById('autoTranslateToggle');
    if (!auto || !auto.checked) { return; }
    const ta = document.getElementById('postText');
    const lang = document.getElementById('langSelect')?.value || 'English';
    const text = (window.easyMDE ? window.easyMDE.value() : ta?.value) || '';
    const target = document.getElementById('translated');
    if (!target) return;

    // Close previous stream
    if (currentES) { try { currentES.close(); } catch { /* no-op */ } currentES = null; }

    // Reset content
    target.textContent = '';

    // Build URL using data attributes if available; fallback to hard-coded route
    const baseUrl = document.body?.dataset?.translateUrl || '/Editor/TranslateStream';
    const url = `${baseUrl}?lang=${encodeURIComponent(lang)}&text=${encodeURIComponent(text)}`;
    const es = new EventSource(url);
    currentES = es;

    es.onmessage = function (e) {
      if (e.data === '__reset__') { target.textContent = ''; return; }
      target.textContent += e.data;
    };
    es.onerror = function () {
      try { es.close(); } catch { /* no-op */ }
    };
  }

  function initEditorPage() {
    const ta = document.getElementById('postText');
    if (!ta || !window.EasyMDE) return;

    window.easyMDE = new EasyMDE({
      element: ta,
      spellChecker: false,
      status: false,
      autofocus: true,
      placeholder: 'Write something interesting...',
      autosave: { enabled: false },
      forceSync: true,
      renderingConfig: {
        singleLineBreaks: true,
        codeSyntaxHighlighting: true
      },
      toolbar: [
        "bold", "italic", "heading", "|", "quote", "unordered-list", "ordered-list", "|",
        {
          name: "save",
          action: function (editor) {
            const params = new URLSearchParams(window.location.search);
            const slug = params.get("slug");
            const language = params.get("language") || "en";
            saveContentToDisk(editor.value(), slug, language);
          },
          className: "bx bx-save",
          title: "Save"
        },
        "|",
        {
          name: "insert-category",
          action: function (editor) {
            const category = prompt("Enter categories separated by commas", "EasyMDE, ASP.NET, C#");
            if (category) {
              const categoryTag = `<!--category-- ${category} -->\n\n`;
              if (window.easyMDE && window.easyMDE.codemirror) {
                window.easyMDE.codemirror.replaceSelection(categoryTag);
              } else {
                editor.value(editor.value() + categoryTag);
              }
            }
          },
          className: "bx bx-tag",
          title: "Insert Categories"
        },
        {
          name: "auto-categorize",
          action: async function (editor) {
            try {
              const text = (window.easyMDE ? window.easyMDE.value() : editor.value()) || '';
              if (!text.trim()) {
                window.showToast && window.showToast('Nothing to analyze. Type something first.', 'info');
                return;
              }
              const resp = await fetch('/Editor/SuggestCategories', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ text: text, max: 6 })
              });
              if (!resp.ok) throw new Error('HTTP ' + resp.status);
              const data = await resp.json();
              const cats = (data && data.categories) || [];
              if (!Array.isArray(cats) || cats.length === 0) {
                window.showToast && window.showToast('No categories suggested.', 'warning');
                return;
              }
              const tag = `<!--category-- ${cats.join(', ')} -->\n\n`;
              if (window.easyMDE && window.easyMDE.codemirror) {
                window.easyMDE.codemirror.replaceSelection(tag);
              } else {
                editor.value(editor.value() + tag);
              }
              window.showToast && window.showToast('Categories inserted.', 'success');
            } catch (e) {
              console.error('Auto-categorize failed', e);
              window.showToast && window.showToast('Auto-categorize failed', 'error');
            }
          },
          className: "bx bx-purchase-tag",
          title: "Auto-categorize with AI"
        },
        "|",
        {
          name: "update",
          action: function () {
            if (window.easyMDE) {
              window.easyMDE.updateTextarea();
            }
            const taEl = document.getElementById('postText');
            if (window.htmx && taEl) {
              htmx.trigger(taEl, 'changed');
              htmx.trigger(taEl, 'keyup');
            }
            if (typeof startTranslationStream === 'function') {
              startTranslationStream();
            } else if (window.editorStartTranslationStream) {
              window.editorStartTranslationStream();
            }
          },
          className: "bx bx-refresh",
          title: "Update"
        },
        "|",
        {
          name: "insert-datetime",
          action: function (editor) {
            const now = new Date();
            const formattedDateTime = now.toISOString().slice(0, 16);
            const datetimeTag = `<datetime class="hidden">${formattedDateTime}</datetime>\n\n`;
            if (window.easyMDE && window.easyMDE.codemirror) {
              window.easyMDE.codemirror.replaceSelection(datetimeTag);
            } else {
              editor.value(editor.value() + datetimeTag);
            }
          },
          className: "bx bx-calendar",
          title: "Insert Datetime"
        },
        "|", "preview", "side-by-side", "fullscreen"
      ]
    });

    const triggerStream = debounce(startTranslationStream, 350);

    window.easyMDE.codemirror.on('change', function () {
      window.easyMDE.updateTextarea();
      if (window.htmx && ta) {
        htmx.trigger(ta, 'changed');
        htmx.trigger(ta, 'keyup');
      }
      triggerStream();
    });

    if (window.htmx && ta) {
      htmx.trigger(ta, 'changed');
      htmx.trigger(ta, 'keyup');
    }

    document.getElementById('langSelect')?.addEventListener('change', startTranslationStream);
    document.getElementById('autoTranslateToggle')?.addEventListener('change', startTranslationStream);

    // Kick off initial stream once editor is ready
    startTranslationStream();
  }

  document.addEventListener('DOMContentLoaded', initEditorPage);

  // Expose for manual retrigger if needed
  window.editorStartTranslationStream = startTranslationStream;
})();
