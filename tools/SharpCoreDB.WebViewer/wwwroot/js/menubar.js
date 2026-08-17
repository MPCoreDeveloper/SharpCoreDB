(() => {
    'use strict';

    const MENU_EVENT = 'scdb:menu-action';
    let openMenu = null;
    let activeMenuItem = null;

    function buildMenu(menuRoot) {
        const topItems = menuRoot.querySelectorAll(':scope > .scdb-menubar__item');
        topItems.forEach((item, index) => {
            item.setAttribute('role', 'menuitem');
            item.setAttribute('tabindex', index === 0 ? '0' : '-1');
            item.setAttribute('data-menu-index', String(index));

            const menuId = item.dataset.menuId;
            if (!menuId) {
                return;
            }

            item.addEventListener('click', (e) => {
                e.stopPropagation();
                toggleMenu(item);
            });

            item.addEventListener('keydown', (e) => handleTopKeydown(e, item, topItems));
            item.addEventListener('mouseenter', () => {
                if (openMenu && openMenu !== item) {
                    openMenu.classList.remove('open');
                    openMenu.removeAttribute('aria-expanded');
                    item.setAttribute('aria-expanded', 'true');
                    item.classList.add('open');
                    openMenu = item;
                    const dd = document.getElementById(menuId);
                    if (dd) { positionDropdown(dd); }
                }
            });
        });
    }

    function positionDropdown(dropdown) {
        const rect = activeMenuItem?.getBoundingClientRect?.();
        if (!rect) { return; }
        dropdown.style.top = `${rect.bottom}px`;
        dropdown.style.left = `${rect.left}px`;
    }

    function toggleMenu(item) {
        if (openMenu === item) {
            closeMenus();
            return;
        }
        closeMenus();
        openMenu = item;
        item.classList.add('open');
        item.setAttribute('aria-expanded', 'true');
        const menuId = item.dataset.menuId;
        const dropdown = document.getElementById(menuId);
        if (dropdown) {
            activeMenuItem = item;
            positionDropdown(dropdown);
            dropdown.classList.add('open');
            // Focus first item
            const first = dropdown.querySelector(':scope > .scdb-menu__item, :scope > .scdb-menu__group');
            if (first) { first.focus(); }
        }
    }

    function closeMenus() {
        if (openMenu) {
            openMenu.classList.remove('open');
            openMenu.removeAttribute('aria-expanded');
        }
        document.querySelectorAll('.scdb-menu-dropdown.open').forEach(d => d.classList.remove('open'));
        openMenu = null;
        activeMenuItem = null;
    }

    function handleTopKeydown(e, item, topItems) {
        const menuId = item.dataset.menuId;
        const dropdown = menuId ? document.getElementById(menuId) : null;

        switch (e.key) {
            case 'ArrowRight': {
                e.preventDefault();
                const next = topItems[parseInt(item.dataset.menuIndex, 10) + 1];
                if (next) { next.focus(); }
                break;
            }
            case 'ArrowLeft': {
                e.preventDefault();
                const prev = topItems[parseInt(item.dataset.menuIndex, 10) - 1];
                if (prev) { prev.focus(); }
                break;
            }
            case 'ArrowDown':
            case 'Enter':
            case ' ':
                e.preventDefault();
                toggleMenu(item);
                break;
            case 'Escape':
                if (openMenu) { closeMenus(); item.focus(); }
                break;
            default:
                break;
        }
        if (dropdown && !dropdown.classList.contains('open') && ['ArrowDown', 'Enter', ' '].includes(e.key)) {
            dropdown.classList.add('open');
        }
    }

    function buildDropdown(dropdown) {
        const items = dropdown.querySelectorAll(':scope > .scdb-menu__item');
        const groups = dropdown.querySelectorAll(':scope > .scdb-menu__group');
        const allFocusables = [...items, ...groups];

        items.forEach((item, idx) => {
            item.setAttribute('role', 'menuitem');
            item.setAttribute('tabindex', idx === 0 ? '0' : '-1');
            item.addEventListener('click', () => {
                const action = item.dataset.action;
                closeMenus();
                if (action) {
                    dispatchAction(action, item.dataset);
                }
            });
            item.addEventListener('keydown', (e) => {
                handleDropdownKeydown(e, item, allFocusables);
            });
        });

        groups.forEach((group, idx) => {
            group.setAttribute('role', 'menuitem');
            group.setAttribute('tabindex', idx === 0 ? '0' : '-1');
            group.addEventListener('click', () => {
                const action = group.dataset.action;
                closeMenus();
                if (action) {
                    dispatchAction(action, group.dataset);
                }
            });
            group.addEventListener('keydown', (e) => {
                handleDropdownKeydown(e, group, allFocusables);
            });
        });
    }

    function handleDropdownKeydown(e, item, allFocusables) {
        const index = allFocusables.indexOf(item);
        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                allFocusables[(index + 1) % allFocusables.length]?.focus();
                break;
            case 'ArrowUp':
                e.preventDefault();
                allFocusables[(index - 1 + allFocusables.length) % allFocusables.length]?.focus();
                break;
            case 'Escape':
                e.preventDefault();
                closeMenus();
                openMenu?.focus();
                break;
            case 'Tab':
                closeMenus();
                break;
            default:
                break;
        }
    }

    function dispatchAction(action, dataset) {
        const detail = {
            action,
            dataset: { ...dataset }
        };
        document.dispatchEvent(new CustomEvent(MENU_EVENT, { detail, bubbles: true }));
    }

    function init() {
        const menuRoot = document.getElementById('scdb-menubar-nav');
        const dropdowns = document.querySelectorAll('.scdb-menu-dropdown');
        if (menuRoot) { buildMenu(menuRoot); }
        dropdowns.forEach(d => buildDropdown(d));

        document.addEventListener('click', (e) => {
            if (openMenu && !e.target.closest('.scdb-menubar') && !e.target.closest('.scdb-menu-dropdown')) {
                closeMenus();
            }
        });

        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') { closeMenus(); }
        });

        window.addEventListener('resize', () => {
            if (openMenu) {
                const dd = document.getElementById(openMenu.dataset.menuId);
                if (dd) { positionDropdown(dd); }
            }
        });
    }

    // Expose for other modules
    window.SharpCoreDBMenu = {
        close: closeMenus,
        open: (menuId) => {
            const item = document.querySelector(`[data-menu-id="${menuId}"]`);
            if (item) { toggleMenu(item); }
        }
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();