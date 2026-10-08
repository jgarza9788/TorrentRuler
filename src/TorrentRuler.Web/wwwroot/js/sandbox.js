// SQL sandbox page: the editor, query history and snippets (browser-local), table/column/helper
// insertion, inline error marking, and the results grid (formatting, resizable columns, hide/show,
// CSV/JSON export).
(function () {
    'use strict';

    var HISTORY_KEY = 'qf.sandbox.history';
    var SNIPPETS_KEY = 'qf.sandbox.snippets';
    var HISTORY_MAX = 20;
    var CELL_MAX = 120;

    // ---- storage (private windows / blocked storage throw; the page must keep working) ----
    function load(key) {
        try { return JSON.parse(window.localStorage.getItem(key) || '[]'); } catch (e) { return null; }
    }
    function save(key, value) {
        try { window.localStorage.setItem(key, JSON.stringify(value)); return true; } catch (e) { return false; }
    }

    var form, textarea, editor;

    function currentSql() { return editor ? editor.getValue() : textarea.value; }
    function setSql(sql) { if (editor) { editor.setValue(sql); editor.focus(); } else { textarea.value = sql; } }
    function insertAtCursor(text) {
        if (editor) {
            editor.replaceSelection(text);
            editor.focus();
        } else {
            var s = textarea.selectionStart ?? textarea.value.length;
            textarea.setRangeText(text, s, textarea.selectionEnd ?? s, 'end');
            textarea.focus();
        }
    }

    // ---- history & snippets menus ----
    function menuItem(label, title, onClick, extra) {
        var li = document.createElement('li');
        li.className = 'd-flex align-items-center';
        var a = document.createElement('button');
        a.type = 'button';
        a.className = 'dropdown-item text-truncate qf-mono-item';
        a.textContent = label;
        if (title) a.title = title;
        a.addEventListener('click', onClick);
        li.appendChild(a);
        if (extra) li.appendChild(extra);
        return li;
    }
    function note(text) {
        var li = document.createElement('li');
        li.innerHTML = '<span class="dropdown-item-text small text-secondary"></span>';
        li.firstChild.textContent = text;
        return li;
    }

    function renderHistory() {
        var menu = document.getElementById('sqlHistoryMenu');
        if (!menu) return;
        menu.innerHTML = '';
        var history = load(HISTORY_KEY);
        if (history === null) { menu.appendChild(note("History isn't available in this browser.")); return; }
        if (history.length === 0) { menu.appendChild(note('Queries you run show up here.')); return; }
        history.forEach(function (sql) {
            menu.appendChild(menuItem(sql.replace(/\s+/g, ' ').slice(0, 90), sql, function () { setSql(sql); }));
        });
    }

    function pushHistory(sql) {
        var history = load(HISTORY_KEY);
        if (history === null || !sql.trim()) return;
        history = [sql].concat(history.filter(function (h) { return h !== sql; })).slice(0, HISTORY_MAX);
        save(HISTORY_KEY, history);
    }

    function renderSnippets() {
        var menu = document.getElementById('sqlSnippetMenu');
        if (!menu) return;
        menu.innerHTML = '';
        var snippets = load(SNIPPETS_KEY);
        if (snippets === null) { menu.appendChild(note("Snippets aren't available in this browser.")); return; }
        menu.appendChild(menuItem('＋ Save current query as…', null, function () {
            var name = window.prompt('Name this snippet:');
            if (!name) return;
            var list = load(SNIPPETS_KEY) || [];
            list = list.filter(function (s) { return s.name !== name; }).concat([{ name: name, sql: currentSql() }]);
            if (save(SNIPPETS_KEY, list) && window.qfToast) window.qfToast('success', 'Saved snippet "' + name + '".');
            renderSnippets();
        }));
        if (snippets.length === 0) { menu.appendChild(note('No saved snippets yet.')); return; }
        var divider = document.createElement('li');
        divider.innerHTML = '<hr class="dropdown-divider">';
        menu.appendChild(divider);
        snippets.forEach(function (snip) {
            var del = document.createElement('button');
            del.type = 'button';
            del.className = 'btn btn-sm btn-ghost-danger me-1';
            del.setAttribute('aria-label', 'Delete snippet ' + snip.name);
            del.innerHTML = '<i class="ti ti-trash" aria-hidden="true"></i>';
            del.addEventListener('click', function (e) {
                e.stopPropagation();
                save(SNIPPETS_KEY, (load(SNIPPETS_KEY) || []).filter(function (s) { return s.name !== snip.name; }));
                renderSnippets();
            });
            menu.appendChild(menuItem(snip.name, snip.sql, function () { setSql(snip.sql); }, del));
        });
    }

    // ---- results grid ----
    var SIZE = /(^|_)(bytes|size)(_|$)|^size/i;
    var RATIO = /(^|_)(ratio|percent|availability)($|_)/i;
    var HASH = /^(torrent_)?hash$/i;
    var ISO = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/;

    function humanBytes(n) {
        if (typeof n !== 'number' || n < 0) return String(n);
        var units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'], i = 0;
        while (n >= 1000 && i < units.length - 1) { n /= 1000; i++; }
        return (i === 0 || n >= 100 ? Math.round(n) : n >= 10 ? n.toFixed(1) : n.toFixed(2)).toString().replace(/\.0+$/, '') + ' ' + units[i];
    }
    function relative(date) {
        var s = (Date.now() - date.getTime()) / 1000, future = s < 0;
        s = Math.abs(s);
        var t = s < 60 ? Math.round(s) + ' s' : s < 3600 ? Math.round(s / 60) + ' min' : s < 86400 ? Math.round(s / 3600) + ' hr' : Math.round(s / 86400) + ' d';
        return future ? 'in ' + t : t + ' ago';
    }

    /** Display form of a cell (raw values are kept for export). Returns { text, title, copy }. */
    function formatCell(column, value) {
        if (value === null || value === undefined) return { text: 'NULL', cls: 'qf-null' };
        if (typeof value === 'number' && SIZE.test(column)) return { text: humanBytes(value), title: value + ' bytes' };
        if (typeof value === 'number' && RATIO.test(column) && !Number.isInteger(value)) return { text: value.toFixed(2), title: String(value) };
        if (typeof value === 'string' && HASH.test(column) && value.length > 12) return { text: value.slice(0, 8) + '…', title: value, copy: value };
        if (typeof value === 'string' && ISO.test(value)) {
            var d = new Date(value);
            if (!isNaN(d)) return { text: d.toLocaleString(), title: value + ' (' + relative(d) + ')' };
        }
        return { text: String(value) };
    }

    function renderGrid(data) {
        var host = document.getElementById('sandboxResults');
        if (!host) return;
        var hidden = {};
        var table = document.createElement('table');
        table.className = 'table table-sm table-striped font-monospace mb-0 qf-results-table';

        var thead = table.createTHead().insertRow();
        var num = document.createElement('th');
        num.className = 'qf-rownum';
        num.textContent = '#';
        thead.appendChild(num);
        data.columns.forEach(function (c, i) {
            var th = document.createElement('th');
            th.dataset.col = i;
            var label = document.createElement('span');
            label.textContent = c;
            th.appendChild(label);
            var grip = document.createElement('span');
            grip.className = 'qf-col-resize';
            grip.setAttribute('aria-hidden', 'true');
            th.appendChild(grip);
            thead.appendChild(th);
        });

        var tbody = table.createTBody();
        data.rows.forEach(function (row, r) {
            var tr = tbody.insertRow();
            var n = tr.insertCell();
            n.className = 'qf-rownum';
            n.textContent = r + 1;
            row.forEach(function (value, i) {
                var td = tr.insertCell();
                td.dataset.col = i;
                var f = formatCell(data.columns[i], value);
                if (f.cls) td.classList.add(f.cls);
                if (f.title) td.title = f.title;
                if (f.text.length > CELL_MAX) {
                    td.textContent = f.text.slice(0, CELL_MAX) + '…';
                    td.classList.add('qf-truncated');
                    td.setAttribute('role', 'button');
                    td.tabIndex = 0;
                    td.title = 'Click to show the whole value';
                    var expand = function () { td.textContent = f.text; td.classList.remove('qf-truncated'); td.removeAttribute('role'); };
                    td.addEventListener('click', expand, { once: true });
                    td.addEventListener('keydown', function (e) { if (e.key === 'Enter') expand(); }, { once: true });
                } else {
                    td.textContent = f.text;
                }
                if (f.copy) {
                    var b = document.createElement('button');
                    b.type = 'button';
                    b.className = 'btn btn-sm btn-ghost-secondary py-0 px-1 ms-1';
                    b.setAttribute('data-copy-text', f.copy);
                    b.setAttribute('aria-label', 'Copy ' + data.columns[i]);
                    b.innerHTML = '<i class="ti ti-copy" aria-hidden="true"></i>';
                    td.appendChild(b);
                }
            });
        });
        host.innerHTML = '';
        host.appendChild(table);

        // Column resize: drag the grip at a header's right edge.
        table.querySelectorAll('.qf-col-resize').forEach(function (grip) {
            grip.addEventListener('mousedown', function (e) {
                var th = grip.parentElement, startX = e.clientX, startW = th.offsetWidth;
                e.preventDefault();
                function move(ev) { th.style.width = th.style.minWidth = Math.max(48, startW + ev.clientX - startX) + 'px'; }
                function up() { document.removeEventListener('mousemove', move); document.removeEventListener('mouseup', up); }
                document.addEventListener('mousemove', move);
                document.addEventListener('mouseup', up);
            });
        });

        // Column hide/show menu.
        var menu = document.getElementById('resultColumnsMenu');
        if (menu) {
            menu.innerHTML = '';
            data.columns.forEach(function (c, i) {
                var id = 'col-toggle-' + i;
                var wrap = document.createElement('div');
                wrap.className = 'form-check';
                wrap.innerHTML = '<input class="form-check-input" type="checkbox" checked><label class="form-check-label"></label>';
                var box = wrap.firstChild, label = wrap.lastChild;
                box.id = id;
                label.htmlFor = id;
                label.textContent = c;
                box.addEventListener('change', function () {
                    hidden[i] = !box.checked;
                    table.querySelectorAll('[data-col="' + i + '"]').forEach(function (cell) { cell.hidden = hidden[i]; });
                });
                menu.appendChild(wrap);
            });
        }

        // Export the raw values (not the display formatting).
        function download(name, type, text) {
            var a = document.createElement('a');
            a.href = URL.createObjectURL(new Blob([text], { type: type }));
            a.download = name;
            document.body.appendChild(a);
            a.click();
            setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 0);
        }
        function csvCell(v) {
            if (v === null || v === undefined) return '';
            var s = String(v);
            return /[",\r\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
        }
        var csvBtn = document.getElementById('exportCsv');
        if (csvBtn) csvBtn.addEventListener('click', function () {
            var lines = [data.columns.map(csvCell).join(',')].concat(data.rows.map(function (r) { return r.map(csvCell).join(','); }));
            download('query-results.csv', 'text/csv', lines.join('\r\n'));
        });
        var jsonBtn = document.getElementById('exportJson');
        if (jsonBtn) jsonBtn.addEventListener('click', function () {
            var objects = data.rows.map(function (r) {
                var o = {};
                data.columns.forEach(function (c, i) { o[c] = r[i]; });
                return o;
            });
            download('query-results.json', 'application/json', JSON.stringify(objects, null, 2));
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        form = document.getElementById('sandboxForm');
        textarea = document.getElementById('Sql');
        if (!form || !textarea) return;

        var hints = [];
        try { hints = JSON.parse(document.getElementById('sandboxHints').textContent); } catch (e) { /* none */ }
        if (window.qfMountSqlEditor) {
            editor = window.qfMountSqlEditor(textarea, { hints: hints, minLines: 8, onRun: function () { form.requestSubmit(); } });
            window.qfSandboxEditor = editor;
        }

        form.addEventListener('submit', function () { pushHistory(currentSql()); });
        renderHistory();
        renderSnippets();

        var useAsRule = document.getElementById('useAsRule');
        if (useAsRule) useAsRule.addEventListener('click', function () {
            location.href = '/Rules/Edit?sql=' + encodeURIComponent(currentSql());
        });

        document.addEventListener('click', function (e) {
            var q = e.target.closest('[data-insert-query]');
            if (q) {
                e.preventDefault(); // inside <summary>: don't also toggle the tree
                var sql = q.getAttribute('data-insert-query');
                if (currentSql().trim() === '') setSql(sql); else insertAtCursor(sql);
                return;
            }
            var ins = e.target.closest('[data-insert]');
            if (ins) insertAtCursor(ins.getAttribute('data-insert'));
        });

        var error = document.getElementById('sandboxError');
        if (error && editor && window.qfMarkSqlError) {
            var pos = window.qfMarkSqlError(editor, error.textContent);
            if (pos) {
                var where = document.createElement('div');
                where.className = 'small mt-1';
                where.textContent = 'Line ' + pos.line + ', column ' + pos.col;
                error.appendChild(where);
            }
        }

        var dataEl = document.getElementById('sandboxData');
        if (dataEl) {
            try { renderGrid(JSON.parse(dataEl.textContent)); } catch (e) { /* leave the grid empty */ }
        }
    });
})();
