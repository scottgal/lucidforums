// Client logic for /Setup page: starts seeding job and renders live progress via SignalR
import * as signalR from '@microsoft/signalr';

function logProgress(box, p) {
  if (!box) return;
  if (box.firstElementChild && box.firstElementChild.classList.contains('text-gray-500')) {
    box.innerHTML = '';
  }
  const line = document.createElement('div');
  const ts = new Date().toLocaleTimeString();
  line.textContent = `[${ts}] ${p.stage}: ${p.message}`;
  box.appendChild(line);
  box.scrollTop = box.scrollHeight;
}

async function ensureConnection(hubPath, onProgress) {
  const connection = new signalR.HubConnectionBuilder().withUrl(hubPath).withAutomaticReconnect().build();
  connection.on('progress', (p) => onProgress(p));
  await connection.start();
  return connection;
}

function initSetupPage(root) {
  const form = root.querySelector('#setupForm');
  const progressBox = root.querySelector('#progress');
  // hub path is provided on a data attribute to avoid inline scripts
  const hubPath = root.querySelector('[data-hub-path]')?.getAttribute('data-hub-path');
  if (!form || !progressBox || !hubPath) return;

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const formData = new FormData(form);
    const res = await fetch(form.action, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
    if (!res.ok) {
      alert('Failed to start seeding');
      return;
    }
    const data = await res.json();
    const jobId = data.jobId;
    const conn = await ensureConnection(hubPath, (p) => logProgress(progressBox, p));
    await conn.invoke('JoinJob', jobId);
    logProgress(progressBox, { stage: 'init', message: 'Subscribed to job ' + jobId });
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => initSetupPage(document));
} else {
  initSetupPage(document);
}

if (window.htmx) {
  window.htmx.onLoad(initSetupPage);
}
