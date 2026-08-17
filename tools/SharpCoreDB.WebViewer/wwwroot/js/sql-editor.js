(() => {
    'use strict';

    /**
     * SharpCoreDB SQL Editor — CSP A+ safe syntax highlighting.
     *
     * Technique: transparent <textarea> overlaid on a mirrored <pre>.
     * The pre is filled with escaped HTML + <span class="tok-*"> tokens.
     * No eval, no inline styles, no third-party libraries.
     */

    const SQL_KEYWORDS = new Set([
        'ADD', 'ALL', 'ALTER', 'AND', 'AS', 'ASC', 'AUTOINCREMENT', 'BETWEEN',
        'BY', 'CASE', 'CHECK', 'COLLATE', 'COLUMN', 'CONSTRAINT', 'CREATE',
        'CROSS', 'CURRENT_DATE', 'CURRENT_TIME', 'CURRENT_TIMESTAMP', 'DATABASE',
        'DEFAULT', 'DEFERRABLE', 'DELETE', 'DESC', 'DISTINCT', 'DROP', 'ELSE',
        'END', 'ESCAPE', 'EXCEPT', 'EXISTS', 'FOREIGN', 'FROM', 'FULL', 'GROUP',
        'HAVING', 'IF', 'IN', 'INDEX', 'INNER', 'INSERT', 'INTERSECT', 'INTO',
        'IS', 'JOIN', 'KEY', 'LEFT', 'LIKE', 'LIMIT', 'NOT', 'NULL', 'ON',
        'OR', 'ORDER', 'OUTER', 'PRIMARY', 'REFERENCES', 'RENAME', 'REPLACE',
        'RIGHT', 'SELECT', 'SET', 'TABLE', 'THEN', 'TO', 'TRANSACTION', 'TRIGGER',
        'UNION', 'UNIQUE', 'UPDATE', 'USING', 'VALUES', 'VIEW', 'WHEN', 'WHERE',
        'WITH', 'WITHOUT', 'EXPLAIN', 'PRAGMA', 'VACUUM', 'ANALYZE', 'ATTACH',
        'DETACH', 'BEGIN', 'COMMIT', 'ROLLBACK', 'SAVEPOINT', 'RELEASE', 'RECURSIVE'
    ]);

    const SQL_TYPES = new Set([
        'INTEGER', 'INT', 'BIGINT', 'SMALLINT', 'TINYINT', 'TEXT', 'VARCHAR',
        'CHAR', 'REAL', 'DOUBLE', 'FLOAT', 'DECIMAL', 'NUMERIC', 'BLOB',
        'BOOLEAN', 'BOOL', 'DATETIME', 'DATE', 'TIME', 'LONG', 'ULID',
        'GUID', 'UUID', 'ROWREF', 'VECTOR', 'JSON', 'JSONB'
    ]);

    const SQL_FUNCTIONS = new Set([
        'ABS', 'AVG', 'COUNT', 'GROUP_CONCAT', 'MAX', 'MIN', 'SUM', 'TOTAL',
        'LENGTH', 'LOWER', 'UPPER', 'TRIM', 'LTRIM', 'RTRIM', 'SUBSTR',
        'REPLACE', 'INSTR', 'HEX', 'PRINTF', 'ROUND', 'RANDOM', 'RANDOMBLOB',
        'ZEROBLOB', 'COALESCE', 'IFNULL', 'NULLIF', 'TYPEOF', 'DATE', 'TIME',
        'DATETIME', 'JULIANDAY', 'STRFTIME', 'UNIXEPOCH', 'SIGN', 'CEIL',
        'FLOOR', 'MOD', 'POW', 'SQRT', 'EXP', 'LN', 'LOG', 'CURRENT_TIME',
        'CURRENT_DATE', 'CURRENT_TIMESTAMP'
    ]);

    const TOKEN_RX = /'[^'\n]*(?:''[^'\n]*)*'|"[^"\n]*(?:""[^"\n]*)*"|\b\d+(?:\.\d+)?\b|\b(?:[A-Za-z_][A-Za-z0-9_]*)\b|--[^\n]*|\/\*[\s\S]*?\*\/|<=|>=|<>|!=|==|=|<|>|\(|\)|,|;|\*|\+|-|\/|%/g;

    let mirror = null;
    let editor = null;
    let lastRendered = '';

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function tokenize(sql) {
        const tokens = [];
        let lastIndex = 0;
        TOKEN_RX.lastIndex = 0;
        let m;

        while ((m = TOKEN_RX.exec(sql)) !== null) {
            if (m.index > lastIndex) {
                tokens.push({ text: sql.slice(lastIndex, m.index), cls: '' });
            }

            const token = m[0];
            const upper = token.toUpperCase();
            let cls = '';

            if (token.startsWith("'")) {
                cls = 'tok-string';
            } else if (token.startsWith('"')) {
                cls = 'tok-ident';
            } else if (token.startsWith('--') || token.startsWith('/*')) {
                cls = 'tok-comment';
            } else if (/^[0-9]/.test(token)) {
                cls = 'tok-number';
            } else if (SQL_KEYWORDS.has(upper)) {
                cls = 'tok-keyword';
            } else if (SQL_TYPES.has(upper)) {
                cls = 'tok-type';
            } else if (SQL_FUNCTIONS.has(upper)) {
                cls = 'tok-function';
            } else if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(token)) {
                // Heuristic: identifier
                cls = 'tok-ident-soft';
            } else {
                cls = 'tok-op';
            }

            tokens.push({ text: token, cls });
            lastIndex = m.index + token.length;
        }

        if (lastIndex < sql.length) {
            tokens.push({ text: sql.slice(lastIndex), cls: '' });
        }

        return tokens;
    }

    function render(sql) {
        const tokens = tokenize(sql);
        let html = '';
        for (const t of tokens) {
            const escaped = escapeHtml(t.text);
            html += t.cls ? `<span class="${t.cls}">${escaped}</span>` : escaped;
        }
        return html;
    }

    function sync() {
        if (!mirror || !editor) { return; }
        const sql = editor.value;
        if (sql === lastRendered) { return; }

        lastRendered = sql;
        mirror.innerHTML = render(sql) + '\n';
        mirror.scrollTop = editor.scrollTop;
        mirror.scrollLeft = editor.scrollLeft;
    }

    function init() {
        editor = document.getElementById('scdb-sql-editor');
        if (!editor) { return; }

        // Find or create mirror element
        mirror = document.getElementById('scdb-sql-mirror');
        if (!mirror) {
            const wrapper = editor.parentElement;
            if (!wrapper) { return; }

            mirror = document.createElement('pre');
            mirror.id = 'scdb-sql-mirror';
            mirror.className = 'scdb-sql-mirror';
            mirror.setAttribute('aria-hidden', 'true');
            wrapper.insertBefore(mirror, editor);
        }

        const syncAndStyle = () => sync();

        editor.addEventListener('input', syncAndStyle);
        editor.addEventListener('scroll', () => {
            mirror.scrollTop = editor.scrollTop;
            mirror.scrollLeft = editor.scrollLeft;
        });
        editor.addEventListener('keydown', (e) => {
            if (e.key === 'Tab') {
                e.preventDefault();
                const start = editor.selectionStart;
                const end = editor.selectionEnd;
                editor.setRangeText('    ', start, end, 'end');
                editor.dispatchEvent(new Event('input'));
            }
        });

        // Initial render
        lastRendered = '';
        syncAndStyle();

        // Re-sync on resize (mirror height handled via CSS: .scdb-sql-mirror { height: 100%; })
        window.addEventListener('resize', sync);

        // Expose API for other modules
        window.SharpCoreDBSqlEditor = {
            getValue: () => editor.value,
            setValue: (v) => {
                editor.value = v ?? '';
                syncAndStyle();
            },
            focus: () => editor.focus(),
            sync
        };
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();