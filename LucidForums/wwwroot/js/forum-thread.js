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
          this._joined = false;
          this.connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .build();

          // Helper to check connected state (handles race conditions during start/reconnect)
          const isConnected = () => this.connection && this.connection.state === window.signalR.HubConnectionState.Connected;

          // Try to join the thread group with limited retries and small backoff
          const joinGroupWithRetry = async (attempt = 1) => {
            if (!this.connection) return;
            if (!isConnected()) {
              if (attempt <= 5) setTimeout(() => joinGroupWithRetry(attempt + 1), attempt * 300);
              return;
            }
            try {
              await this.connection.invoke('JoinThread', String(threadId));
              this._joined = true;
            } catch (err) {
              console.warn('JoinThread failed (attempt ' + attempt + '):', err);
              if (attempt <= 5) setTimeout(() => joinGroupWithRetry(attempt + 1), attempt * 300);
            }
          };

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

          // Re-join the thread group after reconnection
          this.connection.onreconnected(() => {
            this._joined = false;
            joinGroupWithRetry(1);
          });
          this.connection.onclose(() => { this._joined = false; });

          // Start connection with retry; only join after we are connected
          const startWithRetry = async (attempt = 1) => {
            try {
              await this.connection.start();
              await joinGroupWithRetry(1);
            } catch (err) {
              console.error('SignalR connection start failed (attempt ' + attempt + '):', err);
              if (attempt <= 5) setTimeout(() => startWithRetry(attempt + 1), Math.min(1000 * attempt, 5000));
            }
          };

          startWithRetry(1);
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
