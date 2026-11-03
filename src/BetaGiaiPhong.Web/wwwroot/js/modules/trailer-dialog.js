/**
 * Beta Giải Phóng — Official Click-to-Load YouTube Trailer <dialog> (UI-P3)
 * Pure ES Module — Zero jQuery / Zero Bootstrap JS dependencies.
 *
 * Key Guarantees:
 *  - Privacy-enhanced youtube-nocookie.com/embed/{id} iframe is created ONLY
 *    when the user explicitly clicks [data-trailer-open] (zero background video/network load).
 *  - Closing via button, Escape key, or backdrop click immediately resets iframe src
 *    and removes the iframe from the DOM so playback stops cleanly.
 */

const VALID_YOUTUBE_ID_RE = /^[A-Za-z0-9_-]{6,20}$/;

export function initTrailerDialog() {
  const dialog = document.getElementById('bgp-trailer-dialog');
  if (!dialog) return;

  const titleEl = dialog.querySelector('[data-trailer-dialog-title]');
  const stageEl = dialog.querySelector('[data-trailer-stage]');
  const closeBtn = dialog.querySelector('[data-trailer-close]');
  const externalLink = dialog.querySelector('[data-trailer-external-link]');

  if (!stageEl) return;

  /** @type {HTMLElement | null} */
  let lastTrigger = null;

  const clearStage = () => {
    const existingIframe = stageEl.querySelector('iframe');
    if (existingIframe) {
      existingIframe.src = 'about:blank';
    }
    stageEl.replaceChildren();
  };

  const closeDialog = () => {
    clearStage();
    if (typeof dialog.close === 'function' && dialog.open) {
      dialog.close();
    } else {
      dialog.removeAttribute('open');
    }
    if (lastTrigger && document.contains(lastTrigger) && typeof lastTrigger.focus === 'function') {
      lastTrigger.focus();
    }
    lastTrigger = null;
  };

  document.addEventListener('click', (evt) => {
    const target = evt.target instanceof Element ? evt.target.closest('[data-trailer-open]') : null;
    if (!target) return;

    const rawId = (target.getAttribute('data-trailer-open') || '').trim();
    if (!VALID_YOUTUBE_ID_RE.test(rawId)) return;

    evt.preventDefault();
    document.dispatchEvent(new CustomEvent('bgp:close-popover'));

    lastTrigger = /** @type {HTMLElement} */ (target);
    const movieTitle = target.getAttribute('data-trailer-title') || 'Tác phẩm điện ảnh';

    if (titleEl) {
      titleEl.textContent = `Trailer chính thức — ${movieTitle}`;
    }
    if (externalLink instanceof HTMLAnchorElement) {
      externalLink.href = `https://www.youtube.com/watch?v=${encodeURIComponent(rawId)}`;
    }

    clearStage();
    const iframe = document.createElement('iframe');
    iframe.src = `https://www.youtube-nocookie.com/embed/${encodeURIComponent(rawId)}?autoplay=1&rel=0&modestbranding=1`;
    iframe.title = `Trailer chính thức — ${movieTitle}`;
    iframe.allow =
      'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share';
    iframe.allowFullscreen = true;
    iframe.referrerPolicy = 'strict-origin-when-cross-origin';
    iframe.setAttribute('data-testid', 'trailer-iframe');
    stageEl.appendChild(iframe);

    if (typeof dialog.showModal === 'function') {
      if (!dialog.open) {
        dialog.showModal();
      }
    } else {
      dialog.setAttribute('open', 'open');
    }
  });

  if (closeBtn) {
    closeBtn.addEventListener('click', () => {
      closeDialog();
    });
  }

  dialog.addEventListener('close', () => {
    clearStage();
    if (lastTrigger && document.contains(lastTrigger) && typeof lastTrigger.focus === 'function') {
      lastTrigger.focus();
    }
    lastTrigger = null;
  });

  // Backdrop click outside dialog box closes modal cleanly
  dialog.addEventListener('click', (evt) => {
    if (evt.target === dialog) {
      closeDialog();
    }
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initTrailerDialog);
} else {
  initTrailerDialog();
}
