/**
 * Beta Giải Phóng — Netflix-Caliber Floating Quick-Preview Popover (UI-P3)
 * Pure ES Module — Zero jQuery / Zero Bootstrap JS dependencies.
 *
 * Key Guarantees:
 *  - Grounded SSR JSON Island (#bgp-quick-preview-island) with lazy fetch fallback
 *    (/api/movies/{id}/quick-preview) so zero N+1 network storms occur.
 *  - 320ms hover intent ONLY on fine-pointer desktop devices
 *    (matchMedia('(hover: hover) and (pointer: fine)')). Touch devices navigate cleanly.
 *  - Single floating <aside> appended to document.body (never nested inside <a> cards!).
 *  - Strict horizontal & vertical viewport clamping (never clips left/right/top/bottom).
 *  - 100% honest metadata: IMDb rating with scale + vote count, VN age classification,
 *    UTC+7 showtime chips (/showtimes/{id}/seats), and click-to-load trailer trigger.
 */

const HOVER_INTENT_MS = 320;
const HOVER_LEAVE_GRACE_MS = 160;
const VIEWPORT_MARGIN_PX = 12;
const HEADER_SAFE_TOP_PX = 76;

function resolveAgeBadgeModifier(ageLabel) {
  if (!ageLabel) return '';
  const norm = String(ageLabel).trim().toUpperCase();
  if (norm === 'P') return 'bgp-age-badge--p';
  if (norm === 'K') return 'bgp-age-badge--k';
  if (norm === 'T13' || norm === 'C13') return 'bgp-age-badge--t13';
  if (norm === 'T16' || norm === 'C16') return 'bgp-age-badge--t16';
  if (norm === 'T18' || norm === 'C18') return 'bgp-age-badge--t18';
  return 'bgp-age-badge--t13';
}

function canUseDesktopHover() {
  return Boolean(
    window.matchMedia &&
      window.matchMedia('(hover: hover) and (pointer: fine)').matches &&
      window.innerWidth >= 768
  );
}

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = String(text);
  return node;
}

export function initPreviewPopover() {
  const triggers = document.querySelectorAll('[data-preview-movie-id], [data-preview-trigger]');
  if (triggers.length === 0) return;

  /** @type {Map<string, any>} */
  const cache = new Map();
  const islandEl = document.getElementById('bgp-quick-preview-island');
  if (islandEl && islandEl.textContent) {
    try {
      const parsed = JSON.parse(islandEl.textContent);
      if (parsed && typeof parsed === 'object') {
        Object.entries(parsed).forEach(([key, val]) => {
          cache.set(String(key), val);
        });
      }
    } catch {
      // Fallback to on-demand /api/movies/{id}/quick-preview if island JSON is absent
    }
  }

  let popover = document.getElementById('bgp-quick-preview-popover');
  if (!popover) {
    popover = document.createElement('aside');
    popover.id = 'bgp-quick-preview-popover';
    popover.className = 'bgp-preview-popover';
    popover.setAttribute('data-testid', 'quick-preview-popover');
    popover.setAttribute('role', 'dialog');
    popover.setAttribute('aria-modal', 'false');
    popover.setAttribute('aria-label', 'Xem nhanh thông tin phim');
    popover.hidden = true;
    document.body.appendChild(popover);
  }

  let hoverOpenTimer = 0;
  let hoverCloseTimer = 0;
  let activeMovieId = null;
  let activeAnchor = null;

  const clearTimers = () => {
    if (hoverOpenTimer) {
      window.clearTimeout(hoverOpenTimer);
      hoverOpenTimer = 0;
    }
    if (hoverCloseTimer) {
      window.clearTimeout(hoverCloseTimer);
      hoverCloseTimer = 0;
    }
  };

  const closePopover = (restoreFocus = false) => {
    clearTimers();
    popover.classList.remove('is-open');
    popover.hidden = true;
    const prevAnchor = activeAnchor;
    activeMovieId = null;
    activeAnchor = null;

    if (restoreFocus && prevAnchor && typeof prevAnchor.focus === 'function') {
      prevAnchor.focus();
    }
  };

  const scheduleClose = () => {
    if (hoverCloseTimer) window.clearTimeout(hoverCloseTimer);
    hoverCloseTimer = window.setTimeout(() => {
      closePopover(false);
    }, HOVER_LEAVE_GRACE_MS);
  };

  async function loadMoviePreview(movieId) {
    const key = String(movieId);
    if (cache.has(key)) {
      return cache.get(key);
    }

    try {
      const resp = await window.fetch(`/api/movies/${encodeURIComponent(key)}/quick-preview`, {
        headers: { Accept: 'application/json' },
      });
      if (!resp.ok) return null;
      const dto = await resp.json();
      cache.set(key, dto);
      return dto;
    } catch {
      return null;
    }
  }

  function renderPopoverContent(data) {
    popover.replaceChildren();

    // 1. 16:9 Media Stage
    const media = el('div', 'bgp-preview-popover__media');
    const artwork = data.artwork || {};
    const backdropSrc = artwork.backdropW780Url || artwork.backdropUrl;
    const posterSrc = artwork.posterW342Url || artwork.posterUrl;

    if (artwork.hasVerifiedBackdrop && backdropSrc) {
      const img = el('img');
      img.src = backdropSrc;
      img.alt = `Hình nền phim ${data.title}`;
      img.loading = 'eager';
      img.addEventListener('error', () => {
        img.remove();
        media.classList.add('bgp-preview-popover__media--fallback');
      });
      media.appendChild(img);
    } else if (artwork.hasVerifiedPoster && posterSrc) {
      const img = el('img');
      img.src = posterSrc;
      img.alt = `Poster phim ${data.title}`;
      img.style.objectFit = 'contain';
      img.style.background = '#101015';
      img.loading = 'eager';
      img.addEventListener('error', () => {
        img.remove();
        media.classList.add('bgp-preview-popover__media--fallback');
      });
      media.appendChild(img);
    } else {
      media.classList.add('bgp-preview-popover__media--fallback');
      const fallbackNote = el('span', '', 'Chưa có hình ảnh ngang xác minh');
      media.appendChild(fallbackNote);
    }

    media.appendChild(el('div', 'bgp-preview-popover__scrim'));

    const topBar = el('div', 'bgp-preview-popover__top-bar');
    if (data.ageLabel) {
      const ageBadge = el(
        'span',
        `bgp-age-badge ${resolveAgeBadgeModifier(data.ageLabel)}`,
        data.ageLabel
      );
      topBar.appendChild(ageBadge);
    } else {
      const unclassified = el('span', 'bgp-badge', 'Chưa phân loại VN');
      topBar.appendChild(unclassified);
    }

    const closeBtn = el('button', 'bgp-preview-popover__close');
    closeBtn.type = 'button';
    closeBtn.setAttribute('data-testid', 'quick-preview-close');
    closeBtn.setAttribute('aria-label', 'Đóng xem nhanh');
    closeBtn.textContent = '×';
    closeBtn.addEventListener('click', (evt) => {
      evt.stopPropagation();
      closePopover(true);
    });
    topBar.appendChild(closeBtn);
    media.appendChild(topBar);

    const headline = el('div', 'bgp-preview-popover__headline');
    headline.appendChild(el('h3', 'bgp-preview-popover__title', data.title));

    const origParts = [];
    if (
      data.originalTitle &&
      data.originalTitle.toLowerCase() !== String(data.title || '').toLowerCase()
    ) {
      origParts.push(data.originalTitle);
    }
    if (data.releaseYear) {
      origParts.push(String(data.releaseYear));
    }
    if (origParts.length > 0) {
      headline.appendChild(el('p', 'bgp-preview-popover__orig', origParts.join(' • ')));
    }
    media.appendChild(headline);
    popover.appendChild(media);

    // 2. Popover Body
    const body = el('div', 'bgp-preview-popover__body');

    const metaRow = el('div', 'bgp-preview-popover__meta');
    if (data.rating && data.rating.displayText) {
      const ratingBadge = el('span', 'bgp-badge bgp-badge--imdb-solid', data.rating.displayText);
      ratingBadge.setAttribute('data-testid', 'quick-preview-rating');
      metaRow.appendChild(ratingBadge);
    }
    if (data.durationMin) {
      metaRow.appendChild(el('span', '', `${data.durationMin} phút`));
    }
    if (data.genreText) {
      metaRow.appendChild(el('span', '', `• ${data.genreText}`));
    }
    body.appendChild(metaRow);

    if (data.directorNames) {
      const dirLine = el('p', 'bgp-preview-popover__director', 'Đạo diễn: ');
      dirLine.appendChild(el('strong', '', data.directorNames));
      body.appendChild(dirLine);
    }

    const synopsisText =
      data.shortSynopsis || 'Nội dung tóm tắt của tác phẩm hiện đang được cập nhật.';
    body.appendChild(el('p', 'bgp-preview-popover__synopsis', synopsisText));

    // Quick UTC+7 Showtime Pills -> /showtimes/{id}/seats
    const pillsWrap = el('div', 'bgp-time-pills');
    pillsWrap.setAttribute('data-testid', 'quick-preview-showtimes');
    const chips = Array.isArray(data.nextShowtimes) ? data.nextShowtimes.slice(0, 4) : [];

    if (chips.length > 0) {
      chips.forEach((chip) => {
        const label = chip.crossesMidnight
          ? `${chip.startVietnamText} (+1n)`
          : chip.startVietnamText;
        const link = el('a', 'bgp-time-pill bgp-time-pill--highlight', label);
        link.href = `/showtimes/${encodeURIComponent(chip.showtimeId)}/seats`;
        link.title = `${chip.roomName} • Còn ${chip.availableSeatsCount} ghế trống`;
        pillsWrap.appendChild(link);
      });
    } else {
      pillsWrap.appendChild(el('span', 'bgp-badge', 'Chưa có suất chiếu sắp tới'));
    }
    body.appendChild(pillsWrap);

    // Footer Actions
    const footer = el('div', 'bgp-preview-popover__footer');
    const detailLink = el(
      'a',
      'bgp-btn bgp-btn--primary bgp-btn--sm',
      data.upcomingShowtimesCount > 0 ? 'Chọn suất chiếu' : 'Chi tiết phim'
    );
    detailLink.href = `/movies/${encodeURIComponent(data.movieId)}`;
    detailLink.setAttribute('data-testid', 'quick-preview-detail-cta');
    footer.appendChild(detailLink);

    if (data.officialTrailerYoutubeId) {
      const trailerBtn = el('button', 'bgp-btn bgp-btn--glass bgp-btn--sm', 'Xem trailer');
      trailerBtn.type = 'button';
      trailerBtn.setAttribute('data-testid', 'quick-preview-trailer-btn');
      trailerBtn.setAttribute('data-trailer-open', data.officialTrailerYoutubeId);
      trailerBtn.setAttribute('data-trailer-title', data.title);
      footer.appendChild(trailerBtn);
    }

    body.appendChild(footer);
    popover.appendChild(body);
  }

  function positionPopover(anchorEl) {
    popover.hidden = false;
    const anchorRect = anchorEl.getBoundingClientRect();
    const popRect = popover.getBoundingClientRect();

    const vw = window.innerWidth;
    const vh = window.innerHeight;

    const centeredLeft = anchorRect.left + (anchorRect.width - popRect.width) / 2;
    const maxLeft = Math.max(VIEWPORT_MARGIN_PX, vw - popRect.width - VIEWPORT_MARGIN_PX);
    const clampedLeft = Math.min(Math.max(VIEWPORT_MARGIN_PX, centeredLeft), maxLeft);

    const centeredTop = anchorRect.top + (anchorRect.height - popRect.height) / 2;
    const maxTop = Math.max(VIEWPORT_MARGIN_PX, vh - popRect.height - VIEWPORT_MARGIN_PX);
    const minTop = Math.min(HEADER_SAFE_TOP_PX, maxTop);
    const clampedTop = Math.min(Math.max(minTop, centeredTop), maxTop);

    popover.style.left = `${Math.round(clampedLeft)}px`;
    popover.style.top = `${Math.round(clampedTop)}px`;
  }

  async function openForAnchor(movieId, anchorEl, moveFocus = false) {
    const data = await loadMoviePreview(movieId);
    if (!data) return;

    activeMovieId = String(movieId);
    activeAnchor = anchorEl;
    renderPopoverContent(data);
    positionPopover(anchorEl);

    window.requestAnimationFrame(() => {
      if (activeMovieId === String(movieId)) {
        popover.classList.add('is-open');
        if (moveFocus) {
          const closeBtn = popover.querySelector('[data-testid="quick-preview-close"]');
          if (closeBtn) closeBtn.focus();
        }
      }
    });
  }

  // Wire hover intent on cards ([data-preview-movie-id]) for desktop mouse pointers
  document.querySelectorAll('[data-preview-movie-id]').forEach((cardEl) => {
    const movieId = cardEl.getAttribute('data-preview-movie-id');
    if (!movieId) return;

    cardEl.addEventListener('pointerenter', (evt) => {
      if (evt.pointerType && evt.pointerType !== 'mouse') return;
      if (!canUseDesktopHover()) return;

      clearTimers();
      hoverOpenTimer = window.setTimeout(() => {
        openForAnchor(movieId, cardEl, false);
      }, HOVER_INTENT_MS);
    });

    cardEl.addEventListener('pointerleave', (evt) => {
      if (evt.pointerType && evt.pointerType !== 'mouse') return;
      if (hoverOpenTimer) {
        window.clearTimeout(hoverOpenTimer);
        hoverOpenTimer = 0;
      }
      if (activeAnchor === cardEl) {
        scheduleClose();
      }
    });
  });

  // Wire explicit click trigger buttons ([data-preview-trigger])
  document.querySelectorAll('[data-preview-trigger]').forEach((btnEl) => {
    const movieId = btnEl.getAttribute('data-preview-trigger');
    if (!movieId) return;

    btnEl.addEventListener('click', (evt) => {
      evt.preventDefault();
      evt.stopPropagation();
      clearTimers();
      const anchor = btnEl.closest('[data-preview-movie-id]') || btnEl;
      if (activeMovieId === String(movieId) && !popover.hidden) {
        closePopover(false);
      } else {
        openForAnchor(movieId, anchor, true);
      }
    });
  });

  popover.addEventListener('pointerenter', () => {
    if (hoverCloseTimer) {
      window.clearTimeout(hoverCloseTimer);
      hoverCloseTimer = 0;
    }
  });

  popover.addEventListener('pointerleave', (evt) => {
    if (evt.pointerType && evt.pointerType !== 'mouse') return;
    scheduleClose();
  });

  document.addEventListener('keydown', (evt) => {
    if (evt.key === 'Escape' && !popover.hidden) {
      closePopover(true);
    }
  });

  document.addEventListener('bgp:close-popover', () => {
    closePopover(false);
  });

  window.addEventListener(
    'scroll',
    () => {
      if (!popover.hidden) closePopover(false);
    },
    { passive: true }
  );

  document.querySelectorAll('[data-rail-scroller], .bgp-movie-rail, .bgp-wide-rail').forEach((rail) => {
    rail.addEventListener(
      'scroll',
      () => {
        if (!popover.hidden) closePopover(false);
      },
      { passive: true }
    );
  });

  window.addEventListener(
    'resize',
    () => {
      if (!popover.hidden) closePopover(false);
    },
    { passive: true }
  );
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initPreviewPopover);
} else {
  initPreviewPopover();
}
