(() => {
    'use strict';

    const queryTabs = {
        tabs: [],
        activeTabId: null,
        get storageKey() {
            const workspace = document.getElementById('scdb-workspace');
            const scope = workspace?.dataset?.tabScope?.trim() || 'global';
            return `scdb.querytabs.${scope}`;
        }
    };

    const commandPaletteState = {
        commands: [],
        filtered: [],
        selectedIndex: 0,
        selectedTable: ''
    };

    const panelState = {
        connect: { isOpen: true, width: 320 },
        saved: { isOpen: false, width: 320 },
        history: { isOpen: false, width: 320 },
        get storageKey() {
            const workspace = document.getElementById('scdb-workspace');
            const scope = workspace?.dataset?.tabScope?.trim() || 'global';
            return `scdb.panels.${scope}`;
        }
    };

    const snippetState = {
        snippets: [],
        get storageKey() {
            return 'scdb.snippets';
        }
    };

    const paginationState = {
        currentPage: 1,
        pageSize: 100,
        totalRows: 0,
        get storageKey() {
            const workspace = document.getElementById('scdb-workspace');
            const scope = workspace?.dataset?.tabScope?.trim() || 'global';
            return `scdb.pagination.${scope}`;
        }
    };

    function switchResultTab(name) {
        const paneResults = document.getElementById('pane-results');
        const paneMessages = document.getElementById('pane-messages');
        if (name === 'results') {
            paneResults?.removeAttribute('hidden');
            paneMessages?.setAttribute('hidden', '');
        } else {
            paneResults?.setAttribute('hidden', '');
            paneMessages?.removeAttribute('hidden');
        }

        document.getElementById('tab-results')?.classList.toggle('active', name === 'results');
        document.getElementById('tab-messages')?.classList.toggle('active', name === 'messages');
    }

    function switchPanel(name) {
        ['connect', 'saved', 'history'].forEach(p => {
            document.getElementById(`ppane-${p}`)?.classList.toggle('scdb-hidden', p !== name);
            document.getElementById(`ptab-${p}`)?.classList.toggle('active', p === name);
        });
    }

    function createTabTitle(sql, fallbackIndex) {
        const firstLine = (sql || '').split('\n').map(x => x.trim()).find(x => x.length > 0) ?? '';
        if (!firstLine) {
            return `Query ${fallbackIndex}`;
        }

        const normalized = firstLine.replace(/^--\s*/, '').trim();
        return normalized.length <= 28 ? normalized : `${normalized.substring(0, 28)}…`;
    }

    function createQueryTab(sql, title) {
        const id = `tab-${Date.now()}-${Math.floor(Math.random() * 10000)}`;
        return {
            id,
            sql: sql || '',
            title: title || createTabTitle(sql, queryTabs.tabs.length + 1)
        };
    }

    function getEditor() {
        return document.getElementById('scdb-sql-editor');
    }

    function getActiveQueryTab() {
        return queryTabs.tabs.find(t => t.id === queryTabs.activeTabId) ?? null;
    }

    function syncEditorFromActiveTab() {
        const editor = getEditor();
        const active = getActiveQueryTab();
        if (!editor) {
            return;
        }

        editor.value = active?.sql ?? '';
    }

    function syncActiveTabFromEditor() {
        const editor = getEditor();
        const active = getActiveQueryTab();
        if (!editor || !active) {
            return;
        }

        active.sql = editor.value;
        active.title = createTabTitle(active.sql, queryTabs.tabs.indexOf(active) + 1);
        renderQueryTabs();
    }

    function renderQueryTabs() {
        const container = document.getElementById('scdb-query-tabs');
        if (!container) {
            return;
        }

        container.innerHTML = '';

        queryTabs.tabs.forEach((tab, index) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = `scdb-query-tab${tab.id === queryTabs.activeTabId ? ' active' : ''}`;
            button.setAttribute('role', 'tab');
            button.setAttribute('aria-selected', String(tab.id === queryTabs.activeTabId));
            button.title = tab.title || `Query ${index + 1}`;
            button.onclick = () => switchQueryTab(tab.id);
            button.ondblclick = () => renameQueryTab(tab.id);

            const title = document.createElement('span');
            title.className = 'scdb-query-tab__title';
            title.textContent = tab.title || `Query ${index + 1}`;
            button.appendChild(title);

            const close = document.createElement('span');
            close.className = 'scdb-query-tab__close';
            close.textContent = '✕';
            close.title = 'Close tab';
            close.onclick = event => {
                event.stopPropagation();
                closeQueryTab(tab.id);
            };
            button.appendChild(close);

            container.appendChild(button);
        });

        const addButton = document.createElement('button');
        addButton.type = 'button';
        addButton.className = 'scdb-query-tab scdb-query-tab--add';
        addButton.textContent = '+';
        addButton.title = 'New query tab (Ctrl+T)';
        addButton.onclick = () => addNewQueryTab();
        container.appendChild(addButton);
    }

    function persistQueryTabs() {
        try {
            localStorage.setItem(queryTabs.storageKey, JSON.stringify({
                activeTabId: queryTabs.activeTabId,
                tabs: queryTabs.tabs
            }));
        } catch {
        }
    }

    function switchQueryTab(tabId) {
        syncActiveTabFromEditor();
        queryTabs.activeTabId = tabId;
        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function addNewQueryTab(initialSql) {
        syncActiveTabFromEditor();
        const tab = createQueryTab(initialSql || '', null);
        queryTabs.tabs.push(tab);
        queryTabs.activeTabId = tab.id;
        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function closeQueryTab(tabId) {
        if (queryTabs.tabs.length === 1) {
            queryTabs.tabs[0].sql = '';
            queryTabs.tabs[0].title = 'Query 1';
            queryTabs.activeTabId = queryTabs.tabs[0].id;
            syncEditorFromActiveTab();
            renderQueryTabs();
            persistQueryTabs();
            return;
        }

        const index = queryTabs.tabs.findIndex(t => t.id === tabId);
        if (index < 0) {
            return;
        }

        queryTabs.tabs.splice(index, 1);
        if (queryTabs.activeTabId === tabId) {
            queryTabs.activeTabId = queryTabs.tabs[Math.max(0, index - 1)].id;
        }

        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function renameQueryTab(tabId) {
        const tab = queryTabs.tabs.find(t => t.id === tabId);
        if (!tab) {
            return;
        }

        const nextTitle = prompt('Tab name', tab.title);
        if (!nextTitle) {
            return;
        }

        tab.title = nextTitle.trim();
        renderQueryTabs();
        persistQueryTabs();
    }

    function duplicateActiveQueryTab() {
        const active = getActiveQueryTab();
        if (!active) {
            return;
        }

        addNewQueryTab(active.sql);
        const duplicated = getActiveQueryTab();
        if (duplicated) {
            duplicated.title = `${active.title} Copy`;
            renderQueryTabs();
            persistQueryTabs();
        }
    }

    function loadQueryTabs() {
        const editor = getEditor();
        const initialSql = editor?.value ?? '';

        try {
            const raw = localStorage.getItem(queryTabs.storageKey);
            if (raw) {
                const parsed = JSON.parse(raw);
                if (Array.isArray(parsed?.tabs) && parsed.tabs.length > 0) {
                    queryTabs.tabs = parsed.tabs
                        .filter(t => t && typeof t.id === 'string' && typeof t.sql === 'string')
                        .map((t, i) => ({
                            id: t.id,
                            sql: t.sql,
                            title: typeof t.title === 'string' && t.title.trim().length > 0 ? t.title : createTabTitle(t.sql, i + 1)
                        }));

                    queryTabs.activeTabId = queryTabs.tabs.some(t => t.id === parsed.activeTabId)
                        ? parsed.activeTabId
                        : queryTabs.tabs[0].id;

                    syncEditorFromActiveTab();
                    renderQueryTabs();
                    return;
                }
            }
        } catch {
        }

        const initialTab = createQueryTab(initialSql, 'Query 1');
        queryTabs.tabs = [initialTab];
        queryTabs.activeTabId = initialTab.id;
        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function serializeQueryTabsForPost() {
        syncActiveTabFromEditor();

        const activeTabIdInput = document.getElementById('ActiveQueryTabId');
        const stateInput = document.getElementById('QueryTabsStateJson');

        if (activeTabIdInput) {
            activeTabIdInput.value = queryTabs.activeTabId || '';
        }

        if (stateInput) {
            stateInput.value = JSON.stringify({
                activeTabId: queryTabs.activeTabId,
                tabs: queryTabs.tabs
            });
        }

        persistQueryTabs();
    }

    function triggerExecuteShortcut() {
        const form = document.getElementById('form-execute-main');
        if (form && validateAndSubmit(form)) {
            form.submit();
        }
    }

    function getSelectedTableName() {
        const selected = document.querySelector('#scdb-table-list li.selected');
        return selected?.getAttribute('data-table')?.trim() || '';
    }

    function setSelectedTableName(tableName) {
        commandPaletteState.selectedTable = tableName;
    }

    function selectTable(element, tableName) {
        document.querySelectorAll('#scdb-table-list li').forEach(li => li.classList.remove('selected'));
        element.classList.add('selected');

        const hidden = document.querySelector('input[name="SelectedTable"]');
        if (hidden) {
            hidden.value = tableName;
        }

        setSelectedTableName(tableName);
    }

    function appendSqlToActiveTab(sql) {
        const active = getActiveQueryTab();
        if (!active) {
            addNewQueryTab(sql);
            return;
        }

        active.sql = sql;
        active.title = createTabTitle(active.sql, queryTabs.tabs.indexOf(active) + 1);
        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function quoteIdentifier(input) {
        return `"${input.replaceAll('"', '""')}"`;
    }

    function newQueryFromSelection() {
        const table = getSelectedTableName();
        if (!table) {
            return;
        }

        addNewQueryTab(`SELECT * FROM ${quoteIdentifier(table)} LIMIT 100;`);
    }

    function selectTopFromSelection() {
        const table = getSelectedTableName();
        if (!table) {
            return;
        }

        appendSqlToActiveTab(`SELECT * FROM ${quoteIdentifier(table)} LIMIT 100;`);
    }

    function countRowsFromSelection() {
        const table = getSelectedTableName();
        if (!table) {
            return;
        }

        appendSqlToActiveTab(`SELECT COUNT(*) AS TotalRows FROM ${quoteIdentifier(table)};`);
    }

    function toggleGroup(header) {
        const list = header.nextElementSibling;
        const chevron = header.querySelector('.scdb-sidebar__chevron');
        const collapsed = list?.hasAttribute('hidden');
        if (list) {
            if (collapsed) {
                list.removeAttribute('hidden');
            } else {
                list.setAttribute('hidden', '');
            }
        }

        if (chevron) {
            chevron.textContent = collapsed ? '▾' : '▸';
        }

        header.setAttribute('aria-expanded', String(collapsed));
    }

    function toggleConnectionMode(mode) {
        const local = document.getElementById('scdb-local-fields');
        const server = document.getElementById('scdb-server-fields');
        if (!local || !server) {
            return;
        }

        if (mode === 'Local') {
            local.removeAttribute('hidden');
            server.setAttribute('hidden', '');
        } else {
            local.setAttribute('hidden', '');
            server.removeAttribute('hidden');
        }
    }

    function validateAndSubmit(form) {
        const editor = getEditor();
        const msgElement = document.getElementById('scdb-sql-validation-msg');

        serializeQueryTabsForPost();

        const sql = (editor?.value ?? '').trim();

        editor?.classList.remove('is-invalid');
        if (msgElement) {
            msgElement.textContent = '';
            msgElement.classList.add('scdb-hidden');
        }

        if (!sql) {
            editor?.classList.add('is-invalid');
            if (msgElement) {
                msgElement.textContent = 'SQL cannot be empty.';
                msgElement.classList.remove('scdb-hidden');
            }

            editor?.focus();
            return false;
        }

        const dangerPattern = /^\s*(DELETE\s+FROM|UPDATE\s+\S+\s+SET)\s/i;
        if (dangerPattern.test(sql) && !/\bWHERE\b/i.test(sql)) {
            if (!confirm('⚠ This statement has no WHERE clause and will affect all rows.\n\nContinue?')) {
                return false;
            }
        }

        return true;
    }

    function validateConnectionForm(form) {
        const modeElement = form.querySelector('input[name="Connection.ConnectionMode"]:checked');
        const mode = modeElement ? modeElement.value : 'Local';
        let isValid = true;

        if (mode === 'Local') {
            const path = form.querySelector('#Connection_LocalDatabasePath');
            if (path && !path.value.trim()) {
                path.classList.add('is-invalid');
                showFieldError(path, 'Database path is required.');
                isValid = false;
            }

            const password = form.querySelector('#Connection_Password');
            if (password && !password.value.trim()) {
                password.classList.add('is-invalid');
                showFieldError(password, 'Password is required.');
                isValid = false;
            }
        } else {
            [
                ['#Connection_ServerHost', 'Host is required.'],
                ['#Connection_ServerDatabase', 'Database is required.'],
                ['#Connection_ServerUsername', 'Username is required.']
            ].forEach(([selector, message]) => {
                const element = form.querySelector(selector);
                if (element && !element.value.trim()) {
                    element.classList.add('is-invalid');
                    showFieldError(element, message);
                    isValid = false;
                }
            });

            const port = form.querySelector('#Connection_ServerPort');
            if (port) {
                const value = Number.parseInt(port.value, 10);
                if (!value || value < 1 || value > 65535) {
                    port.classList.add('is-invalid');
                    showFieldError(port, 'Port must be 1–65535.');
                    isValid = false;
                }
            }
        }

        return isValid;
    }

    function showFieldError(element, message) {
        let span = element.nextElementSibling;
        if (!span || !span.classList.contains('scdb-validation-message')) {
            span = document.createElement('span');
            span.className = 'scdb-validation-message';
            element.insertAdjacentElement('afterend', span);
        }

        span.textContent = message;
    }

    function clearFieldError(element) {
        element.classList.remove('is-invalid');
        const span = element.nextElementSibling;
        if (span && span.classList.contains('scdb-validation-message')) {
            span.textContent = '';
        }
    }

    function loadHistoryItem(element) {
        const sql = JSON.parse(element.dataset.sql ?? '""');
        if (sql) {
            appendSqlToActiveTab(sql);
        }
    }

    function initHorizontalResizer(splitterId, leftElementId, rightElementId, leftStorageKey, rightStorageKey) {
        const splitter = document.getElementById(splitterId);
        const leftElement = document.getElementById(leftElementId);
        const rightElement = document.getElementById(rightElementId);
        if (!splitter || !leftElement || !rightElement) {
            return;
        }

        try {
            const leftWidth = localStorage.getItem(leftStorageKey);
            if (leftWidth) {
                const parsedLeftWidth = Number.parseInt(leftWidth, 10);
                leftElement.style.width = `${parsedLeftWidth}px`;
                leftElement.style.minWidth = `${parsedLeftWidth}px`;
            }

            const rightWidth = localStorage.getItem(rightStorageKey);
            if (rightWidth) {
                const parsedRightWidth = Number.parseInt(rightWidth, 10);
                rightElement.style.width = `${parsedRightWidth}px`;
                rightElement.style.minWidth = `${parsedRightWidth}px`;
            }
        } catch {
        }

        let startX = 0;
        let startLeftWidth = 0;
        let startRightWidth = 0;

        const onMouseMove = event => {
            const delta = event.clientX - startX;

            if (splitterId === 'scdb-splitter-left') {
                const newWidth = Math.min(460, Math.max(180, startLeftWidth + delta));
                leftElement.style.width = `${newWidth}px`;
                leftElement.style.minWidth = `${newWidth}px`;
                try {
                    localStorage.setItem(leftStorageKey, String(newWidth));
                } catch {
                }
            } else {
                const newWidth = Math.min(420, Math.max(220, startRightWidth - delta));
                rightElement.style.width = `${newWidth}px`;
                rightElement.style.minWidth = `${newWidth}px`;
                try {
                    localStorage.setItem(rightStorageKey, String(newWidth));
                } catch {
                }
            }
        };

        const onMouseUp = () => {
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
            document.body.classList.remove('scdb-resize-active');
        };

        const handleKeyResize = event => {
            const right = splitterId === 'scdb-splitter-left' ? leftElement : rightElement;
            if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
                return;
            }

            event.preventDefault();
            const currentWidth = Math.round(right.getBoundingClientRect().width);
            const delta = event.key === 'ArrowRight' ? 12 : -12;

            if (splitterId === 'scdb-splitter-left') {
                const newWidth = Math.min(460, Math.max(180, currentWidth + delta));
                leftElement.style.width = `${newWidth}px`;
                leftElement.style.minWidth = `${newWidth}px`;
                try {
                    localStorage.setItem(leftStorageKey, String(newWidth));
                } catch {
                }
            } else {
                const newWidth = Math.min(420, Math.max(220, currentWidth - delta));
                rightElement.style.width = `${newWidth}px`;
                rightElement.style.minWidth = `${newWidth}px`;
                try {
                    localStorage.setItem(rightStorageKey, String(newWidth));
                } catch {
                }
            }
        };

        splitter.addEventListener('mousedown', event => {
            startX = event.clientX;
            startLeftWidth = leftElement.getBoundingClientRect().width;
            startRightWidth = rightElement.getBoundingClientRect().width;
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
            document.body.classList.add('scdb-resize-active');
            event.preventDefault();
        });

        splitter.tabIndex = 0;
        splitter.addEventListener('keydown', handleKeyResize);
    }

    function collectResultsFromGrid() {
        const table = document.querySelector('#pane-results table.scdb-grid');
        if (!table) {
            return null;
        }

        const headers = [...table.querySelectorAll('thead th')]
            .map(th => th.textContent?.trim() ?? '')
            .filter((_, index) => index !== 0);

        const rows = [...table.querySelectorAll('tbody tr')].map(tr => {
            const cells = [...tr.querySelectorAll('td')].slice(1);
            return cells.map(td => {
                const nullSpan = td.querySelector('.scdb-grid__null');
                return nullSpan ? null : (td.textContent ?? '').trim();
            });
        });

        return { headers, rows };
    }

    function escapeCsvValue(value) {
        if (value === null || value === undefined) {
            return '';
        }

        const text = String(value);
        if (text.includes(',') || text.includes('"') || text.includes('\n') || text.includes('\r')) {
            return `"${text.replaceAll('"', '""')}"`;
        }

        return text;
    }

    function downloadText(content, fileName, mimeType) {
        const blob = new Blob([content], { type: mimeType });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
    }

    function exportResultsAsCsv() {
        const payload = collectResultsFromGrid();
        if (!payload || payload.rows.length === 0) {
            alert('No result rows available for export.');
            return;
        }

        const lines = [payload.headers.map(escapeCsvValue).join(',')];
        payload.rows.forEach(row => lines.push(row.map(escapeCsvValue).join(',')));
        downloadText(lines.join('\r\n'), 'sharpcoredb-results.csv', 'text/csv;charset=utf-8');
    }

    function exportResultsAsJson() {
        const payload = collectResultsFromGrid();
        if (!payload || payload.rows.length === 0) {
            alert('No result rows available for export.');
            return;
        }

        downloadText(JSON.stringify(payload.rows), 'sharpcoredb-results.json', 'application/json;charset=utf-8');
    }

    async function copyResultsToClipboard() {
        const payload = collectResultsFromGrid();
        if (!payload || payload.rows.length === 0) {
            alert('No result rows available for copy.');
            return;
        }

        const csv = [payload.headers.map(escapeCsvValue).join(',')];
        payload.rows.forEach(row => csv.push(row.map(escapeCsvValue).join(',')));

        await navigator.clipboard.write([
            new ClipboardItem({
                'text/plain': new Blob([csv.join('\r\n')], { type: 'text/plain;charset=utf-8' }),
                'text/csv': new Blob([csv.join('\r\n')], { type: 'text/csv;charset=utf-8' })
            })
        ]);

        alert('Results copied to clipboard.');
    }

    function initSnippetManager() {
        const stored = localStorage.getItem(snippetState.storageKey);
        if (stored) {
            try {
                const parsed = JSON.parse(stored);
                if (Array.isArray(parsed)) {
                    snippetState.snippets = parsed.filter(s => s && typeof s.sql === 'string');
                }
            } catch {
            }
        }

        renderSnippetList();
    }

    function renderSnippetList() {
        const container = document.getElementById('scdb-snippet-list');
        if (!container) {
            return;
        }

        container.innerHTML = '';

        snippetState.snippets.forEach((snippet, index) => {
            const item = document.createElement('div');
            item.className = 'scdb-snippet-item';
            item.draggable = true;
            item.dataset.index = index;
            item.title = snippet.sql;

            const text = document.createElement('span');
            text.className = 'scdb-snippet-text';
            text.textContent = snippet.sql;
            item.appendChild(text);

            const buttons = document.createElement('div');
            buttons.className = 'scdb-snippet-buttons';

            const copyButton = document.createElement('button');
            copyButton.type = 'button';
            copyButton.className = 'scdb-snippet-btn';
            copyButton.title = 'Copy SQL to editor';
            copyButton.onclick = () => appendSqlToActiveTab(snippet.sql);
            copyButton.innerHTML = '📋';

            const deleteButton = document.createElement('button');
            deleteButton.type = 'button';
            deleteButton.className = 'scdb-snippet-btn';
            deleteButton.title = 'Delete snippet';
            deleteButton.onclick = () => deleteSnippet(index);
            deleteButton.innerHTML = '🗑️';

            buttons.appendChild(copyButton);
            buttons.appendChild(deleteButton);
            item.appendChild(buttons);

            container.appendChild(item);
        });
    }

    function savePanelState() {
        try {
            localStorage.setItem(panelState.storageKey, JSON.stringify({
                connect: { isOpen: panelState.connect.isOpen },
                saved:   { isOpen: panelState.saved.isOpen },
                history: { isOpen: panelState.history.isOpen }
            }));
        } catch {
        }
    }

    function loadPanelState() {
        try {
            const raw = localStorage.getItem(panelState.storageKey);
            if (raw) {
                const parsed = JSON.parse(raw);
                ['connect', 'saved', 'history'].forEach(p => {
                    if (parsed[p]?.isOpen !== undefined) {
                        panelState[p].isOpen = parsed[p].isOpen;
                    }
                });
            }
        } catch {
        }
    }

    function renderPanelDockState(panel) {
        const pane = document.getElementById(`ppane-${panel}`);
        const tab  = document.getElementById(`ptab-${panel}`);
        if (!pane || !tab) return;

        if (panelState[panel]?.isOpen === false) {
            pane.classList.add('scdb-hidden');
        } else {
            pane.classList.remove('scdb-hidden');
        }
    }

    function togglePanelDock(panel) {
        if (!panelState[panel]) return;
        panelState[panel].isOpen = !panelState[panel].isOpen;
        renderPanelDockState(panel);
        savePanelState();
    }

    function restorePanelStates() {
        loadPanelState();
        ['connect', 'saved', 'history'].forEach(panel => {
            renderPanelDockState(panel);
        });
    }

    function loadSnippets() {
        try {
            const raw = localStorage.getItem(snippetState.storageKey);
            if (raw) {
                const parsed = JSON.parse(raw);
                if (Array.isArray(parsed)) {
                    snippetState.snippets = parsed.filter(s => s && typeof s.id === 'string' && typeof s.name === 'string' && typeof s.sql === 'string');
                }
            }
        } catch {
        }
    }

    function persistSnippets() {
        try {
            localStorage.setItem(snippetState.storageKey, JSON.stringify(snippetState.snippets));
        } catch {
        }
    }

    function createSnippet(name, sql, category) {
        return {
            id: `snippet-${Date.now()}-${Math.floor(Math.random() * 10000)}`,
            name: name || 'Untitled Snippet',
            sql: sql || '',
            category: category || '',
            created: new Date().toISOString()
        };
    }

    function addSnippet(name, sql, category) {
        const snippet = createSnippet(name, sql, category);
        snippetState.snippets.push(snippet);
        persistSnippets();
        renderSnippetBrowser();
        return snippet;
    }

    function deleteSnippet(snippetId) {
        snippetState.snippets = snippetState.snippets.filter(s => s.id !== snippetId);
        persistSnippets();
        renderSnippetBrowser();
    }

    function insertSnippetToEditor(snippetId) {
        const snippet = snippetState.snippets.find(s => s.id === snippetId);
        if (!snippet) {
            return;
        }

        appendSqlToActiveTab(snippet.sql);
    }

    function openCreateDbDialog() {
        const dialog = document.getElementById('scdb-create-db-dialog');
        if (!dialog) return;

        const nameEl = document.getElementById('create-db-name');
        const pathEl = document.getElementById('create-db-path');
        const passEl = document.getElementById('create-db-password');
        const pathErr = document.getElementById('create-db-path-error');
        const passErr = document.getElementById('create-db-password-error');

        if (nameEl) nameEl.value = '';
        if (pathEl) pathEl.value = '';
        if (passEl) passEl.value = '';
        if (pathErr) pathErr.textContent = '';
        if (passErr) passErr.textContent = '';

        dialog.showModal();
        nameEl?.focus();
    }

    function closeCreateDbDialog() {
        const dialog = document.getElementById('scdb-create-db-dialog');
        if (!dialog) return;
        dialog.close();
    }

    function validateCreateDbForm() {
        let isValid = true;

        const path = (document.getElementById('create-db-path')?.value ?? '').trim();
        const pathError = document.getElementById('create-db-path-error');
        if (!path) {
            if (pathError) pathError.textContent = 'Database path is required.';
            document.getElementById('create-db-path')?.focus();
            isValid = false;
        } else if (pathError) {
            pathError.textContent = '';
        }

        const password = (document.getElementById('create-db-password')?.value ?? '').trim();
        const passwordError = document.getElementById('create-db-password-error');
        if (!password) {
            if (passwordError) passwordError.textContent = 'Password is required.';
            if (isValid) document.getElementById('create-db-password')?.focus();
            isValid = false;
        } else if (passwordError) {
            passwordError.textContent = '';
        }

        return isValid;
    }

    function openSnippetDialog() {
        const dialog = document.getElementById('scdb-snippet-dialog');
        if (!dialog) {
            return;
        }

        document.getElementById('snippet-name').value = '';
        document.getElementById('snippet-category').value = '';
        document.getElementById('snippet-sql').value = '';

        dialog.showModal();
        document.getElementById('snippet-name').focus();
    }

    function closeSnippetDialog() {
        const dialog = document.getElementById('scdb-snippet-dialog');
        if (!dialog) {
            return;
        }

        dialog.close();
    }

    function saveSnippet() {
        const name = (document.getElementById('snippet-name')?.value ?? '').trim();
        const category = (document.getElementById('snippet-category')?.value ?? '').trim();
        const sql = (document.getElementById('snippet-sql')?.value ?? '').trim();

        if (!name) {
            alert('Snippet name is required.');
            return;
        }

        if (!sql) {
            alert('SQL template is required.');
            return;
        }

        addSnippet(name, sql, category);
        closeSnippetDialog();
    }

    function savePaginationState() {
        try {
            localStorage.setItem(paginationState.storageKey, JSON.stringify({
                currentPage: paginationState.currentPage,
                pageSize: paginationState.pageSize,
                totalRows: paginationState.totalRows
            }));
        } catch {
        }
    }

    function loadPaginationState() {
        try {
            const raw = localStorage.getItem(paginationState.storageKey);
            if (raw) {
                const parsed = JSON.parse(raw);
                paginationState.currentPage = parsed.currentPage ?? 1;
                paginationState.pageSize = parsed.pageSize ?? 100;
                paginationState.totalRows = parsed.totalRows ?? 0;
            }
        } catch {
        }
    }

    function setPageSize(pageSize) {
        const size = Number.parseInt(pageSize, 10);
        if (size > 0 && size <= 10000) {
            paginationState.pageSize = size;
            paginationState.currentPage = 1;
            savePaginationState();
            renderPaginationToolbar();
        }
    }

    function goToPage(pageNumber) {
        const page = Number.parseInt(pageNumber, 10);
        const maxPage = Math.ceil(paginationState.totalRows / paginationState.pageSize) || 1;
        if (page >= 1 && page <= maxPage) {
            paginationState.currentPage = page;
            savePaginationState();
            renderPaginationToolbar();
        }
    }

    function setResultTotal(totalRows) {
        paginationState.totalRows = totalRows;
        paginationState.currentPage = 1;
        savePaginationState();
        renderPaginationToolbar();
    }

    function renderPaginationToolbar() {
        const toolbar = document.getElementById('scdb-pagination-toolbar');
        if (!toolbar) {
            return;
        }

        toolbar.innerHTML = '';

        const totalPages = Math.ceil(paginationState.totalRows / paginationState.pageSize) || 1;
        const start = (paginationState.currentPage - 1) * paginationState.pageSize + 1;
        const end = Math.min(paginationState.currentPage * paginationState.pageSize, paginationState.totalRows);

        const wrapper = document.createElement('div');
        wrapper.style.display = 'flex';
        wrapper.style.justifyContent = 'space-between';
        wrapper.style.alignItems = 'center';
        wrapper.style.gap = '12px';
        wrapper.style.fontSize = '12px';

        const info = document.createElement('span');
        info.style.color = 'var(--scdb-chrome-text-mute)';
        info.textContent = paginationState.totalRows === 0
            ? 'No results'
            : `Rows ${start}–${end} of ${paginationState.totalRows}`;
        wrapper.appendChild(info);

        const controls = document.createElement('div');
        controls.style.display = 'flex';
        controls.style.alignItems = 'center';
        controls.style.gap = '8px';

        const prevBtn = document.createElement('button');
        prevBtn.type = 'button';
        prevBtn.className = 'scdb-btn scdb-btn--secondary';
        prevBtn.textContent = '◀ Previous';
        prevBtn.disabled = paginationState.currentPage === 1;
        prevBtn.onclick = () => goToPage(paginationState.currentPage - 1);
        controls.appendChild(prevBtn);

        const pageInput = document.createElement('input');
        pageInput.type = 'number';
        pageInput.className = 'scdb-input scdb-mono';
        pageInput.style.width = '50px';
        pageInput.min = '1';
        pageInput.max = String(totalPages);
        pageInput.value = String(paginationState.currentPage);
        pageInput.onchange = () => goToPage(pageInput.value);
        controls.appendChild(pageInput);

        const pageLabel = document.createElement('span');
        pageLabel.style.color = 'var(--scdb-chrome-text-mute)';
        pageLabel.textContent = `/ ${totalPages}`;
        controls.appendChild(pageLabel);

        const nextBtn = document.createElement('button');
        nextBtn.type = 'button';
        nextBtn.className = 'scdb-btn scdb-btn--secondary';
        nextBtn.textContent = 'Next ▶';
        nextBtn.disabled = paginationState.currentPage === totalPages || totalPages === 0;
        nextBtn.onclick = () => goToPage(paginationState.currentPage + 1);
        controls.appendChild(nextBtn);

        const sep = document.createElement('div');
        sep.style.width = '1px';
        sep.style.height = '20px';
        sep.style.backgroundColor = 'var(--scdb-panel-border)';
        controls.appendChild(sep);

        const sizeLabel = document.createElement('span');
        sizeLabel.style.color = 'var(--scdb-chrome-text-mute)';
        sizeLabel.textContent = 'Per page:';
        controls.appendChild(sizeLabel);

        const sizeSelect = document.createElement('select');
        sizeSelect.className = 'scdb-input scdb-mono';
        sizeSelect.style.width = '60px';
        [50, 100, 250, 500, 1000].forEach(size => {
            const opt = document.createElement('option');
            opt.value = String(size);
            opt.textContent = String(size);
            opt.selected = paginationState.pageSize === size;
            sizeSelect.appendChild(opt);
        });
        sizeSelect.onchange = () => setPageSize(sizeSelect.value);
        controls.appendChild(sizeSelect);

        wrapper.appendChild(controls);
        toolbar.appendChild(wrapper);
    }

    // ── Command palette ──────────────────────────────────────────────────────

    function buildCommandList() {
        return [
            { label: 'Execute Query', hint: 'F5', action: () => triggerExecuteShortcut() },
            { label: 'New Query Tab', hint: 'Ctrl+T', action: () => addNewQueryTab('') },
            { label: 'New Query From Selection', hint: '', action: () => newQueryFromSelection() },
            { label: 'Select Top 100', hint: '', action: () => selectTopFromSelection() },
            { label: 'Count Rows', hint: '', action: () => countRowsFromSelection() },
            { label: 'Export as CSV', hint: '', action: () => exportResultsAsCsv() },
            { label: 'Export as JSON', hint: '', action: () => exportResultsAsJson() },
            { label: 'Add Snippet', hint: '', action: () => openSnippetDialog() },
            { label: 'Close Command Palette', hint: 'Esc', action: () => closeCommandPalette() },
        ];
    }

    function openCommandPalette() {
        const overlay = document.getElementById('scdb-command-palette');
        const input = document.getElementById('scdb-command-input');
        if (!overlay) return;
        commandPaletteState.commands = buildCommandList();
        commandPaletteState.filtered = [...commandPaletteState.commands];
        commandPaletteState.selectedIndex = 0;
        overlay.classList.remove('scdb-hidden');
        overlay.setAttribute('aria-hidden', 'false');
        if (input) {
            input.value = '';
            input.focus();
        }
        renderCommandList();
    }

    function closeCommandPalette() {
        const overlay = document.getElementById('scdb-command-palette');
        if (!overlay) return;
        overlay.classList.add('scdb-hidden');
        overlay.setAttribute('aria-hidden', 'true');
    }

    function renderCommandList() {
        const list = document.getElementById('scdb-command-list');
        if (!list) return;
        list.innerHTML = '';
        commandPaletteState.filtered.forEach((cmd, i) => {
            const li = document.createElement('li');
            li.className = 'scdb-command-palette__item' + (i === commandPaletteState.selectedIndex ? ' scdb-command-palette__item--selected' : '');
            li.setAttribute('role', 'option');
            li.setAttribute('aria-selected', String(i === commandPaletteState.selectedIndex));
            li.innerHTML = `<span class="scdb-command-palette__label">${escapeHtml(cmd.label)}</span>${cmd.hint ? `<span class="scdb-command-palette__hint">${escapeHtml(cmd.hint)}</span>` : ''}`;
            li.addEventListener('click', () => { cmd.action(); closeCommandPalette(); });
            list.appendChild(li);
        });
    }

    function escapeHtml(str) {
        return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function initializeCommandPalette() {
        const input = document.getElementById('scdb-command-input');
        const overlay = document.getElementById('scdb-command-palette');
        if (!input || !overlay) return;

        input.addEventListener('input', () => {
            const q = input.value.trim().toLowerCase();
            commandPaletteState.filtered = q
                ? commandPaletteState.commands.filter(c => c.label.toLowerCase().includes(q))
                : [...commandPaletteState.commands];
            commandPaletteState.selectedIndex = 0;
            renderCommandList();
        });

        input.addEventListener('keydown', e => {
            const len = commandPaletteState.filtered.length;
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                commandPaletteState.selectedIndex = (commandPaletteState.selectedIndex + 1) % len;
                renderCommandList();
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                commandPaletteState.selectedIndex = (commandPaletteState.selectedIndex - 1 + len) % len;
                renderCommandList();
            } else if (e.key === 'Enter') {
                e.preventDefault();
                const cmd = commandPaletteState.filtered[commandPaletteState.selectedIndex];
                if (cmd) { cmd.action(); closeCommandPalette(); }
            } else if (e.key === 'Escape') {
                closeCommandPalette();
            }
        });

        // Close on overlay backdrop click
        overlay.addEventListener('click', e => {
            if (e.target === overlay) closeCommandPalette();
        });
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    function showContextMenu(x, y, tableName) {
        const menu = document.getElementById('scdb-explorer-contextmenu');
        if (!menu) return;
        setSelectedTableName(tableName);
        menu.style.left = `${x}px`;
        menu.style.top = `${y}px`;
        menu.classList.remove('scdb-hidden');
        menu.querySelector('.scdb-contextmenu__item')?.focus();
    }

    function hideContextMenu() {
        const menu = document.getElementById('scdb-explorer-contextmenu');
        if (!menu) return;
        menu.classList.add('scdb-hidden');
    }

    function contextMenuAction(action) {
        hideContextMenu();
        switch (action) {
            case 'new': newQueryFromSelection(); break;
            case 'top': selectTopFromSelection(); break;
            case 'count': countRowsFromSelection(); break;
        }
    }

    function initializeContextMenu() {
        const tableList = document.getElementById('scdb-table-list');
        if (tableList) {
            tableList.addEventListener('contextmenu', e => {
                const li = e.target.closest('li[data-table]');
                if (!li) return;
                e.preventDefault();
                setSelectedTableName(li.dataset.table);
                showContextMenu(e.clientX, e.clientY, li.dataset.table);
            });
        }

        // Close on any outside click or Escape
        document.addEventListener('click', e => {
            const menu = document.getElementById('scdb-explorer-contextmenu');
            if (menu && !menu.contains(e.target)) hideContextMenu();
        });

        document.addEventListener('keydown', e => {
            if (e.key === 'Escape') hideContextMenu();
        });
    }

    // ── Snippet browser (in saved-panel) ─────────────────────────────────────

    function renderSnippetBrowser() {
        renderSnippetList();
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────

    function wireKeyboardShortcuts() {
        document.addEventListener('keydown', e => {
            // Ctrl+Shift+P — command palette
            if (e.ctrlKey && e.shiftKey && e.key === 'P') {
                e.preventDefault();
                openCommandPalette();
                return;
            }
            // F5 or Ctrl+Enter — execute query
            if ((e.key === 'F5' || (e.ctrlKey && e.key === 'Enter')) && !e.shiftKey) {
                const editor = document.getElementById('scdb-sql-editor');
                if (editor && document.activeElement === editor) {
                    e.preventDefault();
                    triggerExecuteShortcut();
                }
                return;
            }
            // Ctrl+T — new query tab
            if (e.ctrlKey && e.key === 't') {
                e.preventDefault();
                addNewQueryTab('');
            }
        });
    }

    // ── Central event wiring (replaces all inline handlers) ─────────────────

    function wireEvents() {
        // Delegate all data-action clicks from document root
        document.addEventListener('click', e => {
            const btn = e.target.closest('[data-action]');
            if (!btn) return;

            const action = btn.dataset.action;

            switch (action) {
                case 'toggle-group':
                    toggleGroup(btn);
                    break;
                case 'select-table': {
                    const table = btn.dataset.table;
                    if (table) selectTable(btn, table);
                    break;
                }
                case 'new-query-selection':
                    newQueryFromSelection();
                    break;
                case 'select-top-selection':
                    selectTopFromSelection();
                    break;
                case 'count-rows-selection':
                    countRowsFromSelection();
                    break;
                case 'open-create-db':
                    openCreateDbDialog();
                    break;
                case 'close-create-db':
                    closeCreateDbDialog();
                    break;
                case 'execute-query':
                    triggerExecuteShortcut();
                    break;
                case 'open-command-palette':
                    openCommandPalette();
                    break;
                case 'switch-result-tab':
                    switchResultTab(btn.dataset.tab);
                    break;
                case 'export-csv':
                    exportResultsAsCsv();
                    break;
                case 'export-json':
                    exportResultsAsJson();
                    break;
                case 'switch-panel':
                    // Ignore if the dock button inside the tab was clicked
                    if (!e.target.closest('[data-action="toggle-panel-dock"]')) {
                        switchPanel(btn.dataset.panel);
                    }
                    break;
                case 'toggle-panel-dock':
                    e.stopPropagation();
                    togglePanelDock(btn.dataset.panel);
                    break;
                case 'open-snippet-dialog':
                    openSnippetDialog();
                    break;
                case 'close-snippet-dialog':
                    closeSnippetDialog();
                    break;
                case 'save-snippet':
                    saveSnippet();
                    break;
                case 'load-history':
                    loadHistoryItem(btn);
                    break;
                case 'ctx-new':
                    contextMenuAction('new');
                    break;
                case 'ctx-top':
                    contextMenuAction('top');
                    break;
                case 'ctx-count':
                    contextMenuAction('count');
                    break;
                case 'dismiss-alert':
                    btn.closest('.scdb-alert')?.remove();
                    break;
                case 'cycle-theme':
                    cycleTheme();
                    break;
            }
        });

        // Table li: keyboard Enter/Space acts as click
        document.getElementById('scdb-table-list')?.addEventListener('keydown', e => {
            if (e.key === 'Enter' || e.key === ' ') {
                const li = e.target.closest('li[data-table]');
                if (li) {
                    e.preventDefault();
                    selectTable(li, li.dataset.table);
                }
            }
        });

        // Prevent browser context menu on table list (JS delegation handles it)
        document.getElementById('scdb-table-list')?.addEventListener('contextmenu', e => {
            e.preventDefault();
        });

        // Connection mode radio buttons
        document.getElementById('radio-mode-local')?.addEventListener('change', e => {
            if (e.target.checked) toggleConnectionMode('Local');
        });
        document.getElementById('radio-mode-server')?.addEventListener('change', e => {
            if (e.target.checked) toggleConnectionMode('Server');
        });

        // Clear field errors on input (delegated)
        const connectForm = document.getElementById('form-connect');
        connectForm?.querySelectorAll('.scdb-input').forEach(input => {
            input.addEventListener('input', () => clearFieldError(input));
        });

        // Form submit handlers
        connectForm?.addEventListener('submit', e => {
            if (!validateConnectionForm(connectForm)) e.preventDefault();
        });

        document.getElementById('form-execute-main')?.addEventListener('submit', e => {
            if (!validateAndSubmit(document.getElementById('form-execute-main'))) e.preventDefault();
        });

        document.getElementById('form-create-db')?.addEventListener('submit', e => {
            if (!validateCreateDbForm()) e.preventDefault();
        });
    }

    // ── Theme ─────────────────────────────────────────────────────────────────
    const THEME_STORAGE_KEY = 'scdb-theme';
    const THEMES = ['dark', 'light', 'system'];
    const THEME_META = {
        dark:   { icon: '🌙', label: 'Dark' },
        light:  { icon: '☀️', label: 'Light' },
        system: { icon: '🖥️', label: 'System' }
    };

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem(THEME_STORAGE_KEY, theme);
        const meta = THEME_META[theme];
        const icon = document.getElementById('scdb-theme-icon');
        const label = document.getElementById('scdb-theme-label');
        if (icon) icon.textContent = meta.icon;
        if (label) label.textContent = meta.label;
    }

    function cycleTheme() {
        const current = document.documentElement.getAttribute('data-theme') || 'dark';
        const next = THEMES[(THEMES.indexOf(current) + 1) % THEMES.length];
        applyTheme(next);
    }

    function initialize() {
        // Restore saved theme (blocking script already set data-theme before paint)
        applyTheme(localStorage.getItem(THEME_STORAGE_KEY) || 'dark');
        loadQueryTabs();
        loadSnippets();
        loadPaginationState();
        wireEvents();
        initializeCommandPalette();
        initializeContextMenu();
        restorePanelStates();
        renderSnippetBrowser();
        renderPaginationToolbar();
        wireKeyboardShortcuts();
        initHorizontalResizer('scdb-splitter-left', 'scdb-object-explorer', 'scdb-right-panel', 'scdb.sidebar.width', 'scdb.rightpanel.width');
        initHorizontalResizer('scdb-splitter-right', 'scdb-object-explorer', 'scdb-right-panel', 'scdb.sidebar.width', 'scdb.rightpanel.width');

        const createDbDialog = document.getElementById('scdb-create-db-dialog');
        if (createDbDialog) {
            createDbDialog.addEventListener('cancel', e => { e.preventDefault(); closeCreateDbDialog(); });
            createDbDialog.addEventListener('click', e => { if (e.target === createDbDialog) closeCreateDbDialog(); });
        }

        const dialog = document.getElementById('scdb-snippet-dialog');
        if (dialog) {
            dialog.addEventListener('cancel', event => {
                event.preventDefault();
                closeSnippetDialog();
            });

            dialog.addEventListener('click', event => {
                if (event.target === dialog) {
                    closeSnippetDialog();
                }
            });
        }

        setTimeout(() => {
            document.getElementById('scdb-alert-status')?.remove();
        }, 5000);
    }

    window.switchResultTab = switchResultTab;
    window.switchPanel = switchPanel;
    window.selectTable = selectTable;
    window.toggleGroup = toggleGroup;
    window.toggleConnectionMode = toggleConnectionMode;
    window.validateAndSubmit = validateAndSubmit;
    window.validateConnectionForm = validateConnectionForm;
    window.showFieldError = showFieldError;
    window.clearFieldError = clearFieldError;
    window.loadHistoryItem = loadHistoryItem;
    window.triggerExecuteShortcut = triggerExecuteShortcut;
    window.newQueryFromSelection = newQueryFromSelection;
    window.selectTopFromSelection = selectTopFromSelection;
    window.countRowsFromSelection = countRowsFromSelection;
    window.exportResultsAsCsv = exportResultsAsCsv;
    window.exportResultsAsJson = exportResultsAsJson;
    window.openCommandPalette = openCommandPalette;
    window.closeCommandPalette = closeCommandPalette;
    window.openCreateDbDialog = openCreateDbDialog;
    window.closeCreateDbDialog = closeCreateDbDialog;
    window.validateCreateDbForm = validateCreateDbForm;
    window.contextMenuAction = contextMenuAction;
    window.togglePanelDock = togglePanelDock;
    window.openSnippetDialog = openSnippetDialog;
    window.closeSnippetDialog = closeSnippetDialog;
    window.saveSnippet = saveSnippet;
    window.setPageSize = setPageSize;
    window.goToPage = goToPage;
    window.setResultTotal = setResultTotal;
    window.initialize = initialize;

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }
})();
