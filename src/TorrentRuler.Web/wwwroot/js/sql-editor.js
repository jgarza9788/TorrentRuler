// Shared SQL editor: CodeMirror over a <textarea>, auto-sized, with autocomplete for field-reference
// keys, helper functions, table names and SQL keywords. Used by the rule editor and the SQL sandbox.
// Needs codemirror.js, mode/sql, addon/edit/matchbrackets and addon/hint/show-hint loaded first.
(function () {
    'use strict';

    var KEYWORDS = ['SELECT', 'DISTINCT', 'FROM', 'WHERE', 'AND', 'OR', 'NOT', 'IN', 'IS', 'NULL', 'LIKE', 'REGEXP',
        'EXISTS', 'JOIN', 'LEFT JOIN', 'ON', 'AS', 'GROUP BY', 'ORDER BY', 'HAVING', 'LIMIT', 'WITH', 'UNION',
        'CASE', 'WHEN', 'THEN', 'ELSE', 'END', 'COUNT(*)', 'MIN(', 'MAX(', 'SUM(', 'AVG(', 'COALESCE(', 'BETWEEN'];

    /**
     * Completion entries from the field catalog (window.qfSourceCatalog shape) and helper list:
     * every "<type>.*.<field>" and "<type>.<instance>.<field>" key, each helper, and each table.
     */
    window.qfCatalogHints = function (catalog, helpers) {
        var hints = [];
        (catalog || []).forEach(function (type) {
            hints.push({ text: type.type, kind: 'table' });
            ['*'].concat(type.instances || []).forEach(function (instance) {
                (type.fields || []).forEach(function (f) {
                    hints.push({ text: type.type + '.' + instance + '.' + f.key, kind: 'field', detail: f.valueType });
                });
            });
        });
        (helpers || []).forEach(function (h) {
            var name = h.signature.split('(')[0];
            hints.push({ text: name + '(', kind: 'helper', detail: h.signature });
        });
        return hints;
    };

    function hintSource(entries) {
        var all = entries.concat(KEYWORDS.map(function (k) { return { text: k, kind: 'keyword' }; }));
        return function (cm) {
            var cur = cm.getCursor();
            var line = cm.getLine(cur.line);
            var start = cur.ch;
            // A "word" here includes dots and '*', so qbittorrent.*.si completes as one key.
            while (start > 0 && /[\w.*]/.test(line.charAt(start - 1))) start--;
            var word = line.slice(start, cur.ch).toLowerCase();
            if (!word) return null;

            var matches = all.filter(function (e) { return e.text.toLowerCase().indexOf(word) === 0; });
            if (matches.length < 50) {
                // Then anything containing it, e.g. "play_count" finds every *.play_count key.
                all.forEach(function (e) {
                    var t = e.text.toLowerCase();
                    if (t.indexOf(word) > 0 && matches.indexOf(e) < 0) matches.push(e);
                });
            }
            if (matches.length === 0 || (matches.length === 1 && matches[0].text.toLowerCase() === word)) return null;

            return {
                list: matches.slice(0, 50).map(function (e) {
                    return {
                        text: e.text,
                        displayText: e.detail ? e.text + '   ' + e.detail : e.text,
                        className: 'qf-hint-' + e.kind
                    };
                }),
                from: CodeMirror.Pos(cur.line, start),
                to: CodeMirror.Pos(cur.line, cur.ch)
            };
        };
    }

    /**
     * Mounts the editor over `textarea` and keeps the textarea's value in sync on every change (htmx
     * posts serialise the form directly and never trigger CodeMirror's own save-on-submit).
     * opts: { hints: [...] from qfCatalogHints, onRun: fn for Ctrl/Cmd-Enter, minLines: 6, maxLines: 30 }.
     */
    window.qfMountSqlEditor = function (textarea, opts) {
        if (!textarea || typeof CodeMirror === 'undefined') return null;
        opts = opts || {};
        var minLines = opts.minLines || 6;
        var maxLines = opts.maxLines || 30;
        var source = hintSource(opts.hints || []);

        var keys = {
            Tab: false, // Tab keeps moving focus: this is one field on a form with many
            'Shift-Tab': false,
            'Ctrl-Space': function (cm) { cm.showHint({ hint: source, completeSingle: false }); }
        };
        if (opts.onRun) {
            keys['Ctrl-Enter'] = function () { opts.onRun(); };
            keys['Cmd-Enter'] = function () { opts.onRun(); };
        }

        var editor = CodeMirror.fromTextArea(textarea, {
            mode: 'text/x-sqlite',
            lineNumbers: true,
            matchBrackets: true,
            lineWrapping: true,
            viewportMargin: Infinity, // render everything so the height can follow the content
            extraKeys: keys
        });
        editor.getWrapperElement().classList.add('qf-sql-editor');

        function autosize() {
            var lines = Math.min(maxLines, Math.max(minLines, editor.lineCount()));
            var height = lines * editor.defaultTextHeight() + 10;
            editor.setSize(null, editor.lineCount() > maxLines ? height : 'auto');
            editor.getWrapperElement().style.minHeight = (minLines * editor.defaultTextHeight() + 10) + 'px';
        }
        editor.on('change', function () {
            editor.save();
            autosize();
            window.qfMarkSqlError(editor, null);
        });
        autosize();

        // Pop the list after a dot, or once two identifier characters are typed.
        editor.on('inputRead', function (cm, change) {
            if (change.origin !== '+input' || cm.state.completionActive) return;
            var cur = cm.getCursor();
            var before = cm.getLine(cur.line).slice(0, cur.ch);
            if (/\.$/.test(before) || /[\w]{2,}$/.test(before)) {
                cm.showHint({ hint: source, completeSingle: false });
            }
        });

        // Follow the app's theme. "Match system" removes the attribute; this Bootstrap build reads only the attribute.
        function syncTheme() {
            editor.setOption('theme', document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'material-darker' : 'default');
        }
        syncTheme();
        new MutationObserver(syncTheme).observe(document.documentElement, { attributeFilter: ['data-bs-theme'] });

        return editor;
    };

    /**
     * Marks where SQLite's error points: for a message containing `near "<token>"`, the first occurrence
     * of <token> in the editor. Returns { line, col } (1-based) or null; message null just clears marks.
     */
    window.qfMarkSqlError = function (editor, message) {
        if (!editor) return null;
        (editor.state.qfErrorMarks || []).forEach(function (m) { m.clear(); });
        editor.state.qfErrorMarks = [];
        if (!message) return null;

        var near = /near "([^"]+)"/.exec(message);
        if (!near) return null;
        var token = near[1];
        var text = editor.getValue();
        var index = text.indexOf(token);
        if (index < 0) index = text.toLowerCase().indexOf(token.toLowerCase());
        if (index < 0) return null;

        var from = editor.posFromIndex(index);
        var to = editor.posFromIndex(index + token.length);
        editor.state.qfErrorMarks.push(editor.markText(from, to, { className: 'qf-sql-error' }));
        return { line: from.line + 1, col: from.ch + 1 };
    };
})();
