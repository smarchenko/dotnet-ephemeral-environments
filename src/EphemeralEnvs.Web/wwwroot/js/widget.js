// Product widget: calls the API from the browser. No framework, no build step.
// The only config is <meta name="api-base-url"> written by the layout (Api__PublicBaseUrl), so the same
// image works in every environment.
(() => {
    'use strict';

    const baseUrl = document.querySelector('meta[name="api-base-url"]').content.replace(/\/+$/, '');
    const list = document.getElementById('product-list');
    const form = document.getElementById('product-form');
    const errorBox = document.getElementById('widget-error');
    const statsBar = document.getElementById('product-stats');
    const content = document.getElementById('widget-content');
    const unavailable = 'API is unavailable';

    const statsUnavailable = 'Statistics are unavailable right now.';
    const featuresTimeoutMs = 2000;

    // GET JSON or null on any failure (network, non-2xx, bad JSON). Flags and stats are optional extras:
    // their failures never reach the error box.
    const getJsonOrNull = async (path) => {
        try {
            const response = await fetch(`${baseUrl}${path}`);
            return response.ok ? await response.json() : null;
        } catch {
            return null;
        }
    };

    // Layout depends on flags, not on which data arrives first:
    // 1. Client flags (allow-listed by the API) are fetched once, in parallel with the products. The first render
    //    waits for them, at most featuresTimeoutMs; failed or late = all flags off, and a late answer is ignored.
    // 2. A flagged block gets its place (with a placeholder) before any data is shown, so nothing shifts later.
    // 3. Products and stats then fill their places independently; a stats failure keeps the place with a message.
    const features = Promise.race([
        getJsonOrNull('/features'),
        new Promise((resolve) => setTimeout(() => resolve(null), featuresTimeoutMs)),
    ]).then((flags) => flags ?? {});

    let statsEnabled = false;

    const layout = features.then((flags) => {
        statsEnabled = flags.productStats === true;
        if (statsEnabled) {
            statsBar.textContent = 'Loading statistics…';
            statsBar.classList.remove('d-none');
        }
        content.classList.remove('invisible');
    });

    const formatTime = (iso) => new Date(iso).toISOString().replace('T', ' ').slice(0, 19) + ' UTC';

    const loadStats = async () => {
        if (!statsEnabled) return;
        const stats = await getJsonOrNull('/products/stats');
        if (!stats || typeof stats.count !== 'number') {
            statsBar.textContent = statsUnavailable;
            return;
        }
        const parts = [`${stats.count} products`];
        if (stats.averagePrice != null) parts.push(`average ${Number(stats.averagePrice).toFixed(2)}`);
        if (stats.newestCreatedAt != null) parts.push(`newest ${formatTime(stats.newestCreatedAt)}`);
        statsBar.textContent = parts.join(' · ');
    };

    // Text only (textContent), never innerHTML: product names come from users.
    const cell = (text, className) => {
        const td = document.createElement('td');
        td.textContent = text;
        if (className) td.className = className;
        return td;
    };

    const showError = (lines) => {
        errorBox.replaceChildren(...lines.map((line) => {
            const div = document.createElement('div');
            div.textContent = line;
            return div;
        }));
        errorBox.classList.remove('d-none');
    };

    const clearError = () => errorBox.classList.add('d-none');

    // ProblemDetails (RFC 9457): the title plus every field error from ValidationProblemDetails.
    const problemLines = async (response) => {
        try {
            const problem = await response.json();
            const fieldErrors = Object.entries(problem.errors ?? {})
                .flatMap(([field, messages]) => messages.map((message) => `${field}: ${message}`));
            return [problem.title ?? `HTTP ${response.status}`, ...fieldErrors];
        } catch {
            return [`HTTP ${response.status}`];
        }
    };

    const render = (products) => {
        if (products.length === 0) {
            const empty = cell('No products yet.', 'text-muted');
            empty.colSpan = 3;
            const tr = document.createElement('tr');
            tr.append(empty);
            list.replaceChildren(tr);
            return;
        }
        list.replaceChildren(...products.map((p) => {
            const tr = document.createElement('tr');
            tr.append(
                cell(p.name),
                cell(Number(p.price).toFixed(2), 'text-end'),
                cell(formatTime(p.createdAt)));
            return tr;
        }));
    };

    // The request starts at once; drawing waits for the layout (flags known), so nothing is drawn before its place.
    const loadProducts = async () => {
        let response;
        try {
            response = await fetch(`${baseUrl}/products`);
        } catch {
            await layout;
            showError([unavailable]);
            list.replaceChildren();
            return;
        }
        await layout;
        if (!response.ok) {
            showError(await problemLines(response));
            return;
        }
        render(await response.json());
    };

    // Products and stats in parallel; stats start as soon as the flags are known.
    const load = () => Promise.all([loadProducts(), layout.then(loadStats)]);

    form.addEventListener('submit', async (event) => {
        event.preventDefault();
        clearError();
        const body = {
            name: form.elements.name.value,
            price: Number(form.elements.price.value),
        };
        let response;
        try {
            response = await fetch(`${baseUrl}/products`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body),
            });
        } catch {
            showError([unavailable]);
            return;
        }
        if (!response.ok) {
            showError(await problemLines(response));
            return;
        }
        form.reset();
        await load();
    });

    load();
})();
