(function(){
  function register(Alpine){
    Alpine.data('forumThread', (threadId, hubUrl) => ({
      connection: null,
      init(){
        if (!window.signalR) {
          console.error('SignalR client not found. Ensure the SignalR script is loaded before forum-thread.js.');
          return;
        }
        try {
          this.connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .build();

          this.connection.on('NewMessage', (evtThreadId, messageId) => {
            if (!evtThreadId || String(evtThreadId).toLowerCase() !== String(threadId).toLowerCase()) return;
            const existing = document.getElementById('msg-' + messageId);
            if (existing) return; // already present
            fetch(`/Threads/Message/${messageId}`)
              .then(r => r.text())
              .then(html => {
                const container = document.querySelector('#messages ul');
                if (!container) return;
                const temp = document.createElement('div');
                temp.innerHTML = html.trim();
                const li = temp.firstElementChild;
                if (!li) return;
                container.appendChild(li);
              })
              .catch(console.error);
          });

          this.connection.start()
            .then(() => { return this.connection.invoke('JoinThread', String(threadId)); })
            .catch(err => { console.error('SignalR connection failed', err); });
        } catch (err) {
          console.error('Failed to initialize SignalR forumThread', err);
        }
      },
      // Optional cleanup if Alpine component is destroyed
      dispose(){
        if (this.connection) {
          try { this.connection.invoke('LeaveThread', String(threadId)); } catch {}
          try { this.connection.stop(); } catch {}
        }
      }
    }));
  }

  if (window.Alpine) {
    register(window.Alpine);
  }
  window.addEventListener('alpine:init', function(){
    if (window.Alpine) register(window.Alpine);
  });
})();
