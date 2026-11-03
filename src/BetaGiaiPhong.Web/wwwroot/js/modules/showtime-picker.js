/**
 * Beta Giải Phóng — Movie Details UTC+7 Showtime Date Picker (UI-P3)
 * Pure ES Module — Zero jQuery / Zero Bootstrap JS dependencies.
 * Progressive Enhancement:
 *  - Without JavaScript, all UTC+7 date groups render visibly and date tab links
 *    navigate via server-side ?date=yyyy-MM-dd#showtimes-section.
 *  - With JavaScript, activates WAI-ARIA tablist keyboard navigation
 *    (ArrowRight/Down, ArrowLeft/Up, Home, End, Space) and pushState/popstate URL sync.
 */

export function initShowtimePicker() {
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

    if (options.focusTab && typeof targetTab.focus === 'function') {
      targetTab.focus();
    }

    if (options.updateHistory && window.history && typeof window.history.pushState === 'function') {
      const url = new URL(window.location.href);
      url.searchParams.set('date', resolvedKey);
      window.history.pushState({ dateKey: resolvedKey }, '', url.toString());
    }
  }

  const currentUrl = new URL(window.location.href);
  const queryDate = currentUrl.searchParams.get('date');
  const serverActiveTab = tabs.find((t) => t.classList.contains('is-active')) || tabs[0];
  const initialDateKey =
    queryDate && tabs.some((t) => t.getAttribute('data-date-key') === queryDate)
      ? queryDate
      : serverActiveTab.getAttribute('data-date-key');

  activateDate(initialDateKey, { updateHistory: false, focusTab: false });

  tabs.forEach((tab, index) => {
    tab.addEventListener('click', (evt) => {
      if (evt.ctrlKey || evt.metaKey || evt.shiftKey || evt.button !== 0) return;
      evt.preventDefault();
      const key = tab.getAttribute('data-date-key');
      activateDate(key, { updateHistory: true, focusTab: false });
    });

    tab.addEventListener('keydown', (evt) => {
      if (evt.key === ' ' || evt.key === 'Spacebar') {
        evt.preventDefault();
        const key = tab.getAttribute('data-date-key');
        activateDate(key, { updateHistory: true, focusTab: true });
      } else if (evt.key === 'ArrowRight' || evt.key === 'ArrowDown') {
        evt.preventDefault();
        const nextTab = tabs[(index + 1) % tabs.length];
        activateDate(nextTab.getAttribute('data-date-key'), {
          updateHistory: true,
          focusTab: true,
        });
      } else if (evt.key === 'ArrowLeft' || evt.key === 'ArrowUp') {
        evt.preventDefault();
        const prevTab = tabs[(index - 1 + tabs.length) % tabs.length];
        activateDate(prevTab.getAttribute('data-date-key'), {
          updateHistory: true,
          focusTab: true,
        });
      } else if (evt.key === 'Home') {
        evt.preventDefault();
        activateDate(tabs[0].getAttribute('data-date-key'), {
          updateHistory: true,
          focusTab: true,
        });
      } else if (evt.key === 'End') {
        evt.preventDefault();
        activateDate(tabs[tabs.length - 1].getAttribute('data-date-key'), {
          updateHistory: true,
          focusTab: true,
        });
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
  document.addEventListener('DOMContentLoaded', initShowtimePicker);
} else {
  initShowtimePicker();
}
