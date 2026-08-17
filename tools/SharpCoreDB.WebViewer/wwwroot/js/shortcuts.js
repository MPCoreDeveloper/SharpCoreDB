(() => {
    'use strict';

    /**
     * SharpCoreDB Shortcuts — SSMS-style keyboard shortcuts.
     * External JS, CSP A+ safe. Dispatches via scdb:menu-action so
     * menu-actions.js handles the actual work.
     */
    const MENU_EVENT = 'scdb:menu-action';

    function dispatch(action) {
        document.dispatchEvent(new CustomEvent(MENU_EVENT, {
            detail: { action, dataset: {} },
            bubbles: true
        }));
    }

    function handleKeydown(e) {
        // In textareas/inputs, F5 triggers default browser refresh. Intercept it.
        if (e.key === 'F5') {
            e.preventDefault();
            dispatch('query.execute');
            return;
        }

        // Ctrl+Enter / Meta+Enter → execute
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            dispatch('query.execute');
            return;
        }

        // Ctrl+T / Meta+T → new query tab
        if (e.key.toLowerCase() === 't' && (e.ctrlKey || e.metaKey) && !e.shiftKey) {
            e.preventDefault();
            dispatch('file.new-query');
            return;
        }

        // Ctrl+Shift+P / F1 → command palette
        if ((e.key === 'p' && (e.ctrlKey || e.metaKey) && e.shiftKey) || e.key === 'F1') {
            e.preventDefault();
            dispatch('view.command-palette');
            return;
        }

        // Ctrl+O / Meta+O → open database (focus connect panel)
        if (e.key.toLowerCase() === 'o' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            dispatch('file.open');
            return;
        }

        // Ctrl+S / Meta+S → save (placeholder for saved queries)
        if (e.key.toLowerCase() === 's' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            dispatch('file.save-query');
            return;
        }

        // Ctrl+K,Ctrl+C → comment selection (editor-level; just let default for now)
        // Ctrl+K,Ctrl+U → uncomment (handled by browser)

        // Alt+F4 → close (native browser behavior; no action needed)
    }

    /**
     * Bubble keydown through document so shortcuts work even when
     * focus is inside Monaco/textarea/inputs.
     */
    document.addEventListener('keydown', handleKeydown, true);

    // Allow other modules to register their own shortcuts
    window.SharpCoreDBShortcuts = {
        dispatch
    };
})();