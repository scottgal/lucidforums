// Handles dynamic model list loading on the Admin AI Test page for both Generation and Translation sections

function loadModels(provider, selectEl) {
  if (!selectEl) return;
  selectEl.innerHTML = '<option value="">(Default)</option>';
  if (!provider) return;
  fetch(`/Admin/AiTest/Models?provider=${encodeURIComponent(provider)}`)
    .then(res => res.ok ? res.json() : null)
    .then(models => {
      if (Array.isArray(models)) {
        for (const m of models) {
          const opt = document.createElement('option');
          opt.value = m;
          opt.textContent = m;
          selectEl.appendChild(opt);
        }
      }
    })
    .catch(() => {});
}

function wire(providerId, modelId) {
  const provider = document.getElementById(providerId);
  const model = document.getElementById(modelId);
  if (!provider || !model) return;
  provider.addEventListener('change', e => loadModels(e.target.value, model));
}

function initAdminAiTestPage(root) {
  // Detect by presence of one of the known selects
  const hasAny = root.querySelector('#providerSelect') || root.querySelector('#providerSelect2') || root.querySelector('#providerSelect3') || document.getElementById('providerSelect') || document.getElementById('providerSelect2') || document.getElementById('providerSelect3');
  if (!hasAny) return;
  wire('providerSelect', 'modelSelect');
  wire('providerSelect2', 'modelSelect2');
  wire('providerSelect3', 'modelSelect3');
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => initAdminAiTestPage(document));
} else {
  initAdminAiTestPage(document);
}

if (window.htmx) {
  window.htmx.onLoad(initAdminAiTestPage);
}
