// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Client-side table filter.
// Any <input data-table-filter> inside a .qf-card hides non-matching rows of that
// card's table as you type. Browser-only: no requests, no server state.
(function () {
    'use strict';

    function initTableFilter(input, index) {
        var card = input.closest('.qf-card');
        var table = card ? card.querySelector('table') : null;
        var tbody = table ? table.querySelector('tbody') : null;
        var cardList = card ? card.querySelector('.qf-row-card-list') : null;
        if (!tbody && !cardList) {
            return;
        }

        var storageKey = 'qf-table-filter:' + window.location.pathname + ':' + index;
        var emptyRow = null;

        function dataRows() {
            return tbody ? Array.prototype.filter.call(tbody.rows, function (row) {
                return !row.classList.contains('qf-table-filter-empty');
            }) : [];
        }

        function cardRows() {
            return cardList ? Array.prototype.filter.call(cardList.children, function (el) {
                return el.classList.contains('qf-row-card');
            }) : [];
        }

        function apply() {
            var q = input.value.trim().toLowerCase();
            var rows = dataRows();
            var anyVisible = false;

            rows.forEach(function (row) {
                var hidden = q !== '' && row.textContent.toLowerCase().indexOf(q) === -1;
                row.hidden = hidden;
                if (!hidden) {
                    anyVisible = true;
                }
            });

            cardRows().forEach(function (el) {
                el.hidden = q !== '' && el.textContent.toLowerCase().indexOf(q) === -1;
            });

            if (tbody && q !== '' && !anyVisible) {
                if (!emptyRow) {
                    emptyRow = document.createElement('tr');
                    emptyRow.className = 'qf-table-filter-empty';
                    var cell = document.createElement('td');
                    cell.colSpan = 99;
                    cell.textContent = 'No matches';
                    emptyRow.appendChild(cell);
                    tbody.appendChild(emptyRow);
                }
                emptyRow.hidden = false;
            } else if (emptyRow) {
                emptyRow.hidden = true;
            }

            try {
                if (q === '') {
                    window.sessionStorage.removeItem(storageKey);
                } else {
                    window.sessionStorage.setItem(storageKey, input.value);
                }
            } catch (e) {
                /* sessionStorage unavailable - ignore */
            }
        }

        input.addEventListener('input', apply);

        try {
            var saved = window.sessionStorage.getItem(storageKey);
            if (saved) {
                input.value = saved;
            }
        } catch (e) {
            /* ignore */
        }

        if (input.value.trim() !== '') {
            apply();
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        var inputs = document.querySelectorAll('input[data-table-filter]');
        Array.prototype.forEach.call(inputs, initTableFilter);
    });
})();

// Toasts. qfToast(kind, message, { undoUrl }) shows one; kind is "success" | "error" | "info".
// Server side, Toasts.Add (full-page posts, via TempData -> #qfQueuedToasts) and Toasts.Trigger
// (htmx, via the HX-Trigger header) both end up here.
(function () {
    'use strict';

    var icons = { success: 'circle-check', error: 'alert-circle', info: 'info-circle' };

    function postUndo(url) {
        var form = document.getElementById('qfUndoForm');
        if (!form) return;
        form.action = url;
        form.submit();
    }

    window.qfToast = function (kind, message, opts) {
        var host = document.getElementById('qfToastStack');
        if (!host || !message) return;
        kind = icons[kind] ? kind : 'info';
        var undoUrl = opts && opts.undoUrl;

        var el = document.createElement('div');
        el.className = 'qf-toast qf-toast-' + kind;
        el.setAttribute('role', kind === 'error' ? 'alert' : 'status');

        var icon = document.createElement('i');
        icon.className = 'ti ti-' + icons[kind] + ' qf-toast-icon';
        icon.setAttribute('aria-hidden', 'true');
        el.appendChild(icon);

        var text = document.createElement('div');
        text.className = 'qf-toast-message';
        text.textContent = message;
        el.appendChild(text);

        if (undoUrl) {
            var undo = document.createElement('button');
            undo.type = 'button';
            undo.className = 'btn btn-sm btn-outline-primary';
            undo.textContent = 'Undo';
            undo.addEventListener('click', function () { postUndo(undoUrl); });
            el.appendChild(undo);
        }

        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'btn-close';
        close.setAttribute('aria-label', 'Dismiss notification');
        close.addEventListener('click', function () { el.remove(); });
        el.appendChild(close);

        host.appendChild(el);
        // Errors stay until dismissed; an undo offer lasts 15s; plain confirmations 5s.
        if (kind !== 'error') {
            setTimeout(function () { el.remove(); }, undoUrl ? 15000 : 5000);
        }
    };

    document.addEventListener('DOMContentLoaded', function () {
        var queued = document.getElementById('qfQueuedToasts');
        if (!queued) return;
        try {
            JSON.parse(queued.textContent).forEach(function (t) {
                window.qfToast(t.kind, t.message, { undoUrl: t.undoUrl });
            });
        } catch (e) { /* malformed payload: nothing to show */ }
    });

    // htmx: toasts from the HX-Trigger header, and an error toast for any failed request.
    document.addEventListener('htmx:afterRequest', function (evt) {
        var xhr = evt.detail && evt.detail.xhr;
        var header = xhr && xhr.getResponseHeader('HX-Trigger');
        if (!header || header.charAt(0) !== '{') return;
        try {
            (JSON.parse(header)['qf-toast'] || []).forEach(function (t) { window.qfToast(t.kind, t.message); });
        } catch (e) { /* not ours */ }
    });

    document.addEventListener('htmx:responseError', function (evt) {
        var xhr = evt.detail && evt.detail.xhr;
        var text = xhr && xhr.responseText ? xhr.responseText.trim() : '';
        if (text.charAt(0) === '<') text = ''; // an HTML error page says nothing useful in a toast
        window.qfToast('error', (text || 'The request failed (HTTP ' + (xhr ? xhr.status : '?') + ').').slice(0, 300));
    });

    document.addEventListener('htmx:sendError', function () {
        window.qfToast('error', 'Could not reach the server.');
    });
})();

// Confirm modal. A form or submit button with data-confirm="<text naming the item>" asks first;
// the form submits only after "Confirm". data-confirm-ok sets the confirm button's label.
(function () {
    'use strict';

    var pending = null; // { form, submitter }

    function modal() {
        var el = document.getElementById('qfConfirm');
        return el && window.tabler && window.tabler.Modal ? window.tabler.Modal.getOrCreateInstance(el) : null;
    }

    // Capture phase, so it runs before the busy-button handler and anything else listening.
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (form.dataset.qfConfirmed === '1') {
            delete form.dataset.qfConfirmed;
            return;
        }
        var submitter = e.submitter || null;
        var source = submitter && submitter.hasAttribute('data-confirm') ? submitter
            : form.hasAttribute('data-confirm') ? form : null;
        if (!source) return;

        var m = modal();
        if (!m) {
            // Modal script missing: fall back to the browser's dialog rather than not asking.
            if (!window.confirm(source.getAttribute('data-confirm'))) e.preventDefault();
            return;
        }

        e.preventDefault();
        e.stopImmediatePropagation();
        pending = { form: form, submitter: submitter };
        document.getElementById('qfConfirmBody').textContent = source.getAttribute('data-confirm');
        document.getElementById('qfConfirmOkLabel').textContent = source.getAttribute('data-confirm-ok') || 'Confirm';
        m.show();
    }, true);

    document.addEventListener('DOMContentLoaded', function () {
        var ok = document.getElementById('qfConfirmOk');
        if (!ok) return;
        ok.addEventListener('click', function () {
            var m = modal();
            if (m) m.hide();
            if (!pending) return;
            var p = pending;
            pending = null;
            p.form.dataset.qfConfirmed = '1';
            if (p.form.requestSubmit) {
                p.form.requestSubmit(p.submitter || undefined);
            } else {
                p.form.submit();
            }
        });
    });
})();

// Antiforgery for htmx: a request from an element outside any form (e.g. the Rules list's inline
// priority box) carries no __RequestVerificationToken field, and Razor Pages rejects the POST.
// Send the page's token as the header ASP.NET Core also accepts.
document.addEventListener('htmx:configRequest', function (evt) {
    var token = document.querySelector('input[name="__RequestVerificationToken"]');
    if (token && evt.detail && evt.detail.headers) {
        evt.detail.headers['RequestVerificationToken'] = token.value;
    }
});

// Clickable rows: a <tr data-href> opens that URL when clicked anywhere that isn't itself
// interactive (links, buttons, inputs, menus keep their own behaviour). Keyboard users get the
// row's own link, so the row itself isn't made focusable.
(function () {
    'use strict';
    document.addEventListener('click', function (e) {
        var row = e.target.closest('tr[data-href]');
        if (!row || e.defaultPrevented || e.button !== 0) return;
        if (e.target.closest('a, button, input, select, textarea, label, form, .dropdown, [data-no-row-click]')) return;
        if (window.getSelection && String(window.getSelection())) return; // selecting text, not clicking
        if (e.ctrlKey || e.metaKey) {
            window.open(row.getAttribute('data-href'), '_blank');
        } else {
            window.location.href = row.getAttribute('data-href');
        }
    });
})();

// Copy buttons: any element with data-copy-text copies that text and confirms with a toast.
// navigator.clipboard needs a secure context (HTTPS or localhost); over plain HTTP on a LAN
// address it doesn't exist, so fall back to a hidden textarea + execCommand.
(function () {
    'use strict';
    function copy(text) {
        if (navigator.clipboard && window.isSecureContext) {
            return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return false; });
        }
        var ta = document.createElement('textarea');
        ta.value = text;
        ta.setAttribute('readonly', '');
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        var ok = false;
        try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
        ta.remove();
        return Promise.resolve(ok);
    }
    window.qfCopy = copy;
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-copy-text]');
        if (!btn) return;
        e.preventDefault();
        copy(btn.getAttribute('data-copy-text')).then(function (ok) {
            if (window.qfToast) window.qfToast(ok ? 'success' : 'error', ok ? 'Copied to clipboard.' : 'Copy failed: select and copy by hand.');
        });
    });
})();

// Bootstrap tooltips for anything marked data-bs-toggle="tooltip" (the title attribute is the text).
document.addEventListener('DOMContentLoaded', function () {
    if (!window.tabler || !window.tabler.Tooltip) return;
    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function (el) {
        window.tabler.Tooltip.getOrCreateInstance(el);
    });
});

// Busy state. A submit button shows a spinner and is disabled while its full-page post is in
// flight. Disabled *after* the submit event has run (setTimeout 0): a disabled submitter is left
// out of the form data, which would drop its name/value or its formaction-chosen handler.
// htmx triggers already spin via .htmx-request (site.css); they also get aria-busy here.
(function () {
    'use strict';

    document.addEventListener('submit', function (e) {
        if (e.defaultPrevented) return;
        var form = e.target;
        var btn = e.submitter;
        if (!btn || form.hasAttribute('data-no-busy') || btn.hasAttribute('data-no-busy')) return;
        setTimeout(function () {
            btn.disabled = true;
            btn.classList.add('qf-busy');
            btn.setAttribute('aria-busy', 'true');
        }, 0);
    });

    // The back/forward cache restores the page with the button still disabled.
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('.qf-busy').forEach(function (b) {
            b.disabled = false;
            b.classList.remove('qf-busy');
            b.removeAttribute('aria-busy');
        });
    });

    document.addEventListener('htmx:beforeRequest', function (evt) {
        var elt = evt.detail && evt.detail.elt;
        if (elt && elt.tagName === 'BUTTON') elt.setAttribute('aria-busy', 'true');
    });

    document.addEventListener('htmx:afterRequest', function (evt) {
        var elt = evt.detail && evt.detail.elt;
        if (elt && elt.tagName === 'BUTTON') elt.removeAttribute('aria-busy');
    });
})();
