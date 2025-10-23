// Home page live updates via SignalR
import * as signalR from '@microsoft/signalr';

window.recentThreads = function() {
    return {
        connection: null,
        lastRefresh: Date.now(),

        async init() {
            console.log('[Home] Initializing live updates');
            await this.setupSignalR();
        },

        async setupSignalR() {
            try {
                this.connection = new signalR.HubConnectionBuilder()
                    .withUrl('/hubs/forum')
                    .withAutomaticReconnect()
                    .build();

                // Listen for new threads
                this.connection.on('NewThread', (data) => {
                    console.log('[Home] New thread received:', data);
                    this.refreshThreadList();
                });

                // Connection state handlers
                this.connection.onreconnecting(() => {
                    console.log('[Home] SignalR reconnecting...');
                    this.updateLiveIndicator('reconnecting');
                });

                this.connection.onreconnected(() => {
                    console.log('[Home] SignalR reconnected');
                    this.updateLiveIndicator('connected');
                    this.refreshThreadList();
                });

                this.connection.onclose(() => {
                    console.log('[Home] SignalR disconnected');
                    this.updateLiveIndicator('disconnected');
                });

                await this.connection.start();
                console.log('[Home] SignalR connected');
                this.updateLiveIndicator('connected');

                // Join the home group to receive updates
                await this.connection.invoke('JoinHome');
                console.log('[Home] Joined home group');
            } catch (err) {
                console.error('[Home] SignalR connection error:', err);
                this.updateLiveIndicator('error');
            }
        },

        refreshThreadList() {
            // Throttle refreshes to max once per 2 seconds
            const now = Date.now();
            if (now - this.lastRefresh < 2000) {
                console.log('[Home] Throttling refresh');
                return;
            }
            this.lastRefresh = now;

            console.log('[Home] Refreshing thread list');
            // Trigger HTMX refresh
            document.body.dispatchEvent(new CustomEvent('refreshThreads'));
        },

        updateLiveIndicator(status) {
            const indicator = document.getElementById('live-indicator');
            if (!indicator) return;

            const icon = indicator.querySelector('i');
            if (!icon) return;

            // Remove all status classes
            icon.classList.remove('text-success', 'text-warning', 'text-error', 'bx-wifi', 'bx-wifi-off', 'bx-loader-alt', 'bx-spin');

            switch (status) {
                case 'connected':
                    icon.classList.add('bx-wifi', 'text-success');
                    break;
                case 'reconnecting':
                    icon.classList.add('bx-loader-alt', 'bx-spin', 'text-warning');
                    break;
                case 'disconnected':
                case 'error':
                    icon.classList.add('bx-wifi-off', 'text-error');
                    break;
            }
        },

        destroy() {
            if (this.connection) {
                this.connection.invoke('LeaveHome').catch(err =>
                    console.error('[Home] Error leaving home group:', err)
                );
                this.connection.stop();
            }
        }
    };
};

// Cleanup on page navigation
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        window.addEventListener('beforeunload', () => {
            const list = document.getElementById('recent-threads-list');
            if (list && list.__x) {
                list.__x.$data.destroy();
            }
        });
    });
} else {
    window.addEventListener('beforeunload', () => {
        const list = document.getElementById('recent-threads-list');
        if (list && list.__x) {
            list.__x.$data.destroy();
        }
    });
}
