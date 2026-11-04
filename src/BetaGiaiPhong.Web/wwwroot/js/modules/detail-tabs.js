/**
 * Beta Giải Phóng — Movie Detail Sub-Tabs, Share Link & Ambient Trailer Stage
 * Pure ES Module — Zero Bootstrap / Zero jQuery dependencies.
 */
(() => {
  const tabBar = document.querySelector('[data-detail-tabbar]');
  const centerCol = document.querySelector('[data-detail-center]');

  const TAB_CARD_MAP = {
    info: ['showtimes', 'cast'],
    cast: ['cast'],
    trailer: ['trailer-inline'],
    recommended: ['recommended-grid'],
  };

  let activateTabFn = null;

  if (tabBar && centerCol) {
    const tabs = Array.from(tabBar.querySelectorAll('[role="tab"][data-detail-tab]'));
    const cards = Array.from(centerCol.querySelectorAll('[data-detail-card]'));

    function activateTab(tabKey, focusTab = false) {
      const normalized = TAB_CARD_MAP[tabKey] ? tabKey : 'info';
      const visibleKinds = new Set(TAB_CARD_MAP[normalized]);

      tabs.forEach((btn) => {
        const isMatch = btn.getAttribute('data-detail-tab') === normalized;
        btn.classList.toggle('is-active', isMatch);
        btn.setAttribute('aria-selected', isMatch ? 'true' : 'false');
        btn.setAttribute('tabindex', isMatch ? '0' : '-1');
        if (isMatch && focusTab) {
          btn.focus();
        }
      });

      centerCol.setAttribute('data-active-tab', normalized);

      cards.forEach((card) => {
        const kind = card.getAttribute('data-detail-card') || '';
        const shouldShow = visibleKinds.has(kind);
        card.classList.toggle('is-tab-hidden', !shouldShow);
        if (shouldShow) {
          card.removeAttribute('hidden');
        } else {
          card.setAttribute('hidden', 'hidden');
          // Tear down any inline iframe inside hidden trailer card
          if (kind === 'trailer-inline') {
            const mount = card.querySelector('[data-inline-trailer-mount]');
            const posterBtn = card.querySelector('[data-inline-trailer-play]');
            if (mount) mount.innerHTML = '';
            if (posterBtn) posterBtn.removeAttribute('hidden');
          }
        }
      });
    }

    activateTabFn = activateTab;
    activateTab('info', false);

    tabs.forEach((tab, idx) => {
      tab.addEventListener('click', () => {
        const key = tab.getAttribute('data-detail-tab') || 'info';
        activateTab(key, false);
      });

      tab.addEventListener('keydown', (evt) => {
        if (evt.key === 'ArrowRight') {
          evt.preventDefault();
          const next = tabs[(idx + 1) % tabs.length];
          activateTab(next.getAttribute('data-detail-tab') || 'info', true);
        } else if (evt.key === 'ArrowLeft') {
          evt.preventDefault();
          const prev = tabs[(idx - 1 + tabs.length) % tabs.length];
          activateTab(prev.getAttribute('data-detail-tab') || 'info', true);
        } else if (evt.key === 'Home') {
          evt.preventDefault();
          activateTab(tabs[0].getAttribute('data-detail-tab') || 'info', true);
        } else if (evt.key === 'End') {
          evt.preventDefault();
          activateTab(tabs[tabs.length - 1].getAttribute('data-detail-tab') || 'info', true);
        }
      });
    });
  }

  // Ensure clicking "Chọn suất chiếu" on Hero switches back to 'info' tab if another tab was active
  const scrollCta = document.querySelector('[data-testid="detail-cta-scroll-showtimes"]');
  if (scrollCta) {
    scrollCta.addEventListener('click', () => {
      if (typeof activateTabFn === 'function') {
        activateTabFn('info', false);
      }
    });
  }

  // Click-to-load Inline Trailer in "Hồ sơ & Trailer" tab
  const inlinePlayBtn = document.querySelector('[data-inline-trailer-play]');
  if (inlinePlayBtn instanceof HTMLElement) {
    inlinePlayBtn.addEventListener('click', () => {
      const ytId = inlinePlayBtn.getAttribute('data-youtube-id');
      const title = inlinePlayBtn.getAttribute('data-movie-title') || 'Official Trailer';
      const mount = document.querySelector('[data-inline-trailer-mount]');
      if (!ytId || !mount) return;

      const iframe = document.createElement('iframe');
      iframe.src = `https://www.youtube-nocookie.com/embed/${encodeURIComponent(ytId)}?autoplay=1&rel=0`;
      iframe.title = `Trailer chính thức — ${title}`;
      iframe.allow = 'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture';
      iframe.allowFullscreen = true;
      iframe.className = 'bgp-detail-inline-trailer__iframe';
      mount.innerHTML = '';
      mount.appendChild(iframe);
      inlinePlayBtn.setAttribute('hidden', 'hidden');
    });
  }

  // Share button copies clean URL with subtle feedback badge
  const shareBtn = document.querySelector('[data-detail-share-btn]');
  const shareFeedback = document.querySelector('[data-detail-share-feedback]');
  if (shareBtn instanceof HTMLElement) {
    let feedbackTimer = 0;
    shareBtn.addEventListener('click', async () => {
      const shareUrl = window.location.href.split('#')[0];
      try {
        if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
          await navigator.clipboard.writeText(shareUrl);
        }
      } catch {
        // Ignore clipboard permission errors in headless/restricted contexts
      }

      if (shareFeedback instanceof HTMLElement) {
        shareFeedback.removeAttribute('hidden');
        window.clearTimeout(feedbackTimer);
        feedbackTimer = window.setTimeout(() => {
          shareFeedback.setAttribute('hidden', 'hidden');
        }, 2200);
      }
    });
  }
})();
