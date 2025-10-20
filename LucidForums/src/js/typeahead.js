export function typeahead(dataset) {
    const {
        searchEndpoint,
        searchElementId,
        htmxEndpoint = null,
        htmxTarget = "#page-content",
        redirectUrl = null
    } = dataset;

    console.log("Typeahead initialized for", searchElementId);

    return {
        query: '',
        results: [],
        highlightedIndex: -1,
        endpoint: searchEndpoint,
        elementId: searchElementId,
        redirectUrl,
        htmxEndpoint,
        htmxTarget,
        loading : false,

        search() {
            if (this.query.length < 2) {
                this.results = [];
                this.highlightedIndex = -1;
                return;
            }

            // Abort the previous request if it exists
            if (this.controller) {
                this.controller.abort();
            }

            this.controller = new AbortController();
            const signal = this.controller.signal;

            this.loading = true;

            fetch(`${this.endpoint}/${encodeURIComponent(this.query)}`, {
                method: 'GET',
                headers: { 'Content-Type': 'application/json' },
                signal: signal
            })
                .then(response => {


                    if (response.ok) return response.json();
                    if (response.status === 404) {
                        this.results = [];
                        this.highlightedIndex = -1;
                        return [];
                    }
                    return Promise.reject(response);
                })
                .then(data => {
                    this.results = data;
                    this.highlightedIndex = -1;
                })
                .catch((error) => {

                    if (error.name === 'AbortError') {
                        // Silently handle cancellation
                        return;
                    }

                    if (error.status === 400) {
                        console.warn('400: Reloading page');
                        window.location.reload();
                        return;
                    }
                    if (error.status === 404) return;

                    error.json?.().then((json) => {
                        console.error(json);
                    }).catch(() => {});

                    console.error("Error fetching search results");
                })
                .finally(() => {
                    this.loading = false;
                });
        },

        moveDown() {
            if (this.highlightedIndex < this.results.length - 1) {
                this.highlightedIndex++;
            }
        },

        moveUp() {
            if (this.highlightedIndex > 0) {
                this.highlightedIndex--;
            }
        },

        onEnter() {
            if (this.highlightedIndex >= 0 && this.highlightedIndex < this.results.length) {
                this.selectResult(this.highlightedIndex);
            } else {
                this.submitQuery();
            }
        },

        submitQuery() {
            const q = (this.query || '').trim();
            if (!q) return;

            if (this.redirectUrl) {
                window.location.href = this.redirectUrl.replace("{query}", encodeURIComponent(q));
                return;
            }

            if (this.htmxEndpoint && this.htmxTarget) {
                const targetUrl = this.htmxEndpoint.replace("{query}", encodeURIComponent(q));
                let target = this.htmxTarget.trim();
                if (!target.startsWith("#") && !target.startsWith(".")) {
                    target = `#${target}`;
                }

                const triggerElt = document.getElementById(this.elementId);
                const detail = {
                    elt: triggerElt,
                    path: targetUrl,
                    verb: 'GET',
                    requestConfig: {},
                    headers: {}
                };
                const configEvent = new CustomEvent('htmx:configRequest', { detail, bubbles: true });
                document.body.dispatchEvent(configEvent);

                htmx.ajax('GET', targetUrl, {
                    target: target,
                    headers: {
                        "HTMX-Search": true,
                        ...detail.headers
                    },
                    swap: 'innerHTML',
                    pushUrl: true
                });

                const onSwap = (evt) => {
                    if (evt.target.matches(target)) {
                        document.dispatchEvent(new CustomEvent('sweetalert:close'));
                        document.removeEventListener('htmx:afterSwap', onSwap);
                    }
                };
                document.addEventListener('htmx:afterSwap', onSwap);
            }

            this.results = [];
            this.highlightedIndex = -1;
            this.query = '';
        },

        selectHighlighted() {
            if (this.highlightedIndex >= 0 && this.highlightedIndex < this.results.length) {
                this.selectResult(this.highlightedIndex);
            }
        },

        selectResult(index) {
            const selectedItem = this.results[index];

            if (this.redirectUrl) {
                window.location.href = this.redirectUrl.replace("{query}", encodeURIComponent(selectedItem.id));
                return;
            }

            if (this.htmxEndpoint && this.htmxTarget) {
                const targetUrl = this.htmxEndpoint.replace("{query}", encodeURIComponent(selectedItem.id));
                let target = this.htmxTarget.trim();
                if (!target.startsWith("#") && !target.startsWith(".")) {
                    target = `#${target}`;
                }
                
                const triggerElt = document.getElementById(this.elementId);
                const detail = {
                    elt: triggerElt,
                    path: targetUrl,
                    verb: 'GET',
                    requestConfig: {},
                    headers: {}
                };
                const configEvent = new CustomEvent('htmx:configRequest', { detail, bubbles: true });
                document.body.dispatchEvent(configEvent);
                
                htmx.ajax('GET', targetUrl, {
                    target: target,
                    headers: {
                        "HTMX-Search": true,
                        ...detail.headers 
                    },
                    swap: 'innerHTML',
                    pushUrl: true
                });

                const onSwap = (evt) => {
                    if (evt.target.matches(target)) {
                        document.dispatchEvent(new CustomEvent('sweetalert:close'));
                        document.removeEventListener('htmx:afterSwap', onSwap);
                    }
                };
                document.addEventListener('htmx:afterSwap', onSwap);
            }

            this.results = [];
            this.highlightedIndex = -1;
            this.query = '';
        }

    };
}