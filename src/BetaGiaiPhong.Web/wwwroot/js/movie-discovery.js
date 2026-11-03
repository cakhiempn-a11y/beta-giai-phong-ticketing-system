/**
 * Beta Giai Phong Cinema Ticketing System
 * UI-02B: Movie Details Interactive Date Selector (Progressive Enhancement)
 */
(() => {
    'use strict';

    function initShowtimeDateSelector() {
        const section = document.getElementById('showtimes-section');
        if (!section) return;

        const tablist = section.querySelector('[role="tablist"]');
        if (!tablist) return;

        const tabs = Array.from(tablist.querySelectorAll('[role="tab"][data-date-key]'));
        const panels = Array.from(section.querySelectorAll('[data-date-panel]'));

        if (tabs.length === 0 || panels.length === 0) return;

        section.classList.add('bgp-js-tabs-ready');

        function activateDate(dateKey, options = { updateHistory: false, focusTab: false }) {
            const targetTab = tabs.find((t) => t.getAttribute('data-date-key') === dateKey) || tabs[0];
            if (!targetTab) return;

            const resolvedKey = targetTab.getAttribute('data-date-key');

            tabs.forEach((tab) => {
                const isMatch = tab.getAttribute('data-date-key') === resolvedKey;
                tab.classList.toggle('is-active', isMatch);
                tab.setAttribute('aria-selected', isMatch ? 'true' : 'false');
            });

            panels.forEach((panel) => {
                const isMatch = panel.getAttribute('data-date-panel') === resolvedKey;
                panel.classList.toggle('is-active', isMatch);
                if (isMatch) {
                    panel.removeAttribute('hidden');
                } else {
                    panel.setAttribute('hidden', 'hidden');
                }
            });

            if (options.focusTab) {
                targetTab.focus();
            }

            if (options.updateHistory && window.history && typeof window.history.pushState === 'function') {
                const url = new URL(window.location.href);
                url.searchParams.set('date', resolvedKey);
                window.history.pushState({ dateKey: resolvedKey }, '', url.toString());
            }
        }

        // Determine initial date from URL query or server-selected attribute
        const currentUrl = new URL(window.location.href);
        const queryDate = currentUrl.searchParams.get('date');
        const serverActiveTab = tabs.find((t) => t.classList.contains('is-active')) || tabs[0];
        const initialDateKey =
            (queryDate && tabs.some((t) => t.getAttribute('data-date-key') === queryDate))
                ? queryDate
                : serverActiveTab.getAttribute('data-date-key');

        activateDate(initialDateKey, { updateHistory: false, focusTab: false });

        tabs.forEach((tab, index) => {
            tab.addEventListener('click', (e) => {
                // Allow Ctrl/Cmd/Shift/Middle click to open normally
                if (e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;
                e.preventDefault();
                const key = tab.getAttribute('data-date-key');
                activateDate(key, { updateHistory: true, focusTab: false });
            });

            tab.addEventListener('keydown', (e) => {
                if (e.key === ' ' || e.key === 'Spacebar') {
                    e.preventDefault();
                    const key = tab.getAttribute('data-date-key');
                    activateDate(key, { updateHistory: true, focusTab: true });
                } else if (e.key === 'ArrowRight' || e.key === 'ArrowDown') {
                    e.preventDefault();
                    const nextTab = tabs[(index + 1) % tabs.length];
                    const key = nextTab.getAttribute('data-date-key');
                    activateDate(key, { updateHistory: true, focusTab: true });
                } else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') {
                    e.preventDefault();
                    const prevTab = tabs[(index - 1 + tabs.length) % tabs.length];
                    const key = prevTab.getAttribute('data-date-key');
                    activateDate(key, { updateHistory: true, focusTab: true });
                } else if (e.key === 'Home') {
                    e.preventDefault();
                    const firstKey = tabs[0].getAttribute('data-date-key');
                    activateDate(firstKey, { updateHistory: true, focusTab: true });
                } else if (e.key === 'End') {
                    e.preventDefault();
                    const lastKey = tabs[tabs.length - 1].getAttribute('data-date-key');
                    activateDate(lastKey, { updateHistory: true, focusTab: true });
                }
            });
        });

        window.addEventListener('popstate', () => {
            const url = new URL(window.location.href);
            const popDate = url.searchParams.get('date');
            const fallbackKey = tabs[0].getAttribute('data-date-key');
            activateDate(popDate || fallbackKey, { updateHistory: false, focusTab: false });
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initShowtimeDateSelector);
    } else {
        initShowtimeDateSelector();
    }
})();
