// Handles dynamic model list loading on the Admin AI Settings page

function loadModels(provider, selectEl, endpointBase) {
  if (!selectEl) return;
  const keep = selectEl.value;
  selectEl.innerHTML = '<option value="">(Default)</option>';
  if (!provider) return;
  fetch(`${endpointBase}?provider=${encodeURIComponent(provider)}`)
    .then(res => res.ok ? res.json() : null)
    .then(models => {
      if (Array.isArray(models)) {
        for (const m of models) {
          const opt = document.createElement('option');
          opt.value = m;
          opt.textContent = m;
          if (m === keep) opt.selected = true;
          selectEl.appendChild(opt);
        }
      }
    })
    .catch(() => {});
}

function wireSelect(providerId, modelId) {
  const provider = document.getElementById(providerId);
  const model = document.getElementById(modelId);
  if (!provider || !model) return;
  provider.addEventListener('change', e => {
    loadModels(e.target.value, model, '/Admin/AiSettings/Models');
  });
}

function initAdminAiSettingsPage(root) {
  // Use presence of any of these IDs to detect page
  if (!root.querySelector('#genProvider') && !document.getElementById('genProvider')) return;
  wireSelect('genProvider', 'genModel');
  wireSelect('trProvider', 'trModel');
  wireSelect('embProvider', 'embModel');
}

// Run on initial page load
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => initAdminAiSettingsPage(document));
} else {
  initAdminAiSettingsPage(document);
}

// Also run on HTMX swaps
if (window.htmx) {
  window.htmx.onLoad(initAdminAiSettingsPage);
}
