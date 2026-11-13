// Product widget: calls the API from the browser. No framework, no build step.
// The only config is <meta name="api-base-url"> written by the layout (Api__PublicBaseUrl), so the same
// image works in every environment.
(() => {
    'use strict';

    const baseUrl = document.querySelector('meta[name="api-base-url"]').content.replace(/\/+$/, '');
    const list = document.getElementById('product-list');
    const form = document.getElementById('product-form');
    const errorBox = document.getElementById('widget-error');
    const unavailable = 'API is unavailable';

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
                cell(new Date(p.createdAt).toISOString().replace('T', ' ').slice(0, 19) + ' UTC'));
            return tr;
        }));
    };

    const load = async () => {
        let response;
        try {
            response = await fetch(`${baseUrl}/products`);
        } catch {
            showError([unavailable]);
            list.replaceChildren();
            return;
        }
        if (!response.ok) {
            showError(await problemLines(response));
            return;
        }
        render(await response.json());
    };

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
