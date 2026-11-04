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

const SVG_NS = 'http://www.w3.org/2000/svg';

function createPlayIconSvg() {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('width', '16');
  svg.setAttribute('height', '16');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('fill', 'currentColor');
  svg.setAttribute('aria-hidden', 'true');
  const poly = document.createElementNS(SVG_NS, 'polygon');
  poly.setAttribute('points', '6 4 20 12 6 20 6 4');
  svg.appendChild(poly);
  return svg;
}

function createTrailerIconSvg() {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('width', '16');
  svg.setAttribute('height', '16');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2');
  svg.setAttribute('stroke-linecap', 'round');
  svg.setAttribute('stroke-linejoin', 'round');
  svg.setAttribute('aria-hidden', 'true');
  const rect = document.createElementNS(SVG_NS, 'rect');
  rect.setAttribute('x', '2');
  rect.setAttribute('y', '4');
  rect.setAttribute('width', '20');
  rect.setAttribute('height', '16');
  rect.setAttribute('rx', '2');
  const poly = document.createElementNS(SVG_NS, 'polygon');
  poly.setAttribute('points', '10 9 15 12 10 15 10 9');
  poly.setAttribute('fill', 'currentColor');
  svg.appendChild(rect);
  svg.appendChild(poly);
  return svg;
}

function createChevronDownIconSvg() {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('width', '18');
  svg.setAttribute('height', '18');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2.4');
  svg.setAttribute('stroke-linecap', 'round');
  svg.setAttribute('stroke-linejoin', 'round');
  svg.setAttribute('aria-hidden', 'true');
  const poly = document.createElementNS(SVG_NS, 'polyline');
  poly.setAttribute('points', '6 9 12 15 18 9');
  svg.appendChild(poly);
  return svg;
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
      img.style.background = '#181818';
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
    topBar.appendChild(el('span'));

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

    // 2. Popover Body (Figma Node 109:9726 — #181818 surface, circular icon row + metadata + dot-separated genres)
    const body = el('div', 'bgp-preview-popover__body');

    // Row 1: Netflix Circular Icon Action Row (Figma Node 109:9687)
    const actionsRow = el('div', 'bgp-preview-popover__actions');
    const actionsLeft = el('div', 'bgp-preview-popover__actions-left');

    const primaryLabel =
      data.upcomingShowtimesCount > 0 ? 'Chọn suất chiếu' : 'Chi tiết phim';
    const detailLink = el('a', 'bgp-preview-circle-btn bgp-preview-circle-btn--primary');
    detailLink.href = `/movies/${encodeURIComponent(data.movieId)}`;
    detailLink.setAttribute('data-testid', 'quick-preview-detail-cta');
    detailLink.setAttribute('aria-label', `${primaryLabel}: ${data.title}`);
    detailLink.title = primaryLabel;
    detailLink.appendChild(createPlayIconSvg());
    actionsLeft.appendChild(detailLink);

    if (data.officialTrailerYoutubeId) {
      const trailerBtn = el('button', 'bgp-preview-circle-btn');
      trailerBtn.type = 'button';
      trailerBtn.setAttribute('data-testid', 'quick-preview-trailer-btn');
      trailerBtn.setAttribute('data-trailer-open', data.officialTrailerYoutubeId);
      trailerBtn.setAttribute('data-trailer-title', data.title);
      trailerBtn.setAttribute('aria-label', `Xem trailer chính thức: ${data.title}`);
      trailerBtn.title = 'Xem trailer chính thức';
      trailerBtn.appendChild(createTrailerIconSvg());
      actionsLeft.appendChild(trailerBtn);
    }

    actionsRow.appendChild(actionsLeft);

    const expandLink = el('a', 'bgp-preview-circle-btn');
    expandLink.href = `/movies/${encodeURIComponent(data.movieId)}#showtimes-section`;
    expandLink.setAttribute('aria-label', `Chi tiết phim và lịch chiếu: ${data.title}`);
    expandLink.title = 'Chi tiết phim & lịch chiếu';
    expandLink.appendChild(createChevronDownIconSvg());
    actionsRow.appendChild(expandLink);

    body.appendChild(actionsRow);

    // Row 2: Honest Cinema Status (#46D369) + VN Age Classification + Duration + IMDb Rating
    const metaRow = el('div', 'bgp-preview-popover__meta');
    if (data.upcomingShowtimesCount > 0) {
      metaRow.appendChild(
        el('span', 'bgp-preview-popover__status', `${data.upcomingShowtimesCount} suất đang mở bán`)
      );
    } else {
      metaRow.appendChild(el('span', '', 'Chưa mở lịch chiếu'));
    }

    if (data.ageLabel) {
      const ageBadge = el(
        'span',
        `bgp-age-badge ${resolveAgeBadgeModifier(data.ageLabel)}`,
        data.ageLabel
      );
      metaRow.appendChild(ageBadge);
    }

    if (data.durationMin) {
      metaRow.appendChild(el('span', '', `${data.durationMin} phút`));
    }

    if (data.rating && data.rating.displayText) {
      const ratingBadge = el('span', 'bgp-badge bgp-badge--imdb-solid', data.rating.displayText);
      ratingBadge.setAttribute('data-testid', 'quick-preview-rating');
      metaRow.appendChild(ratingBadge);
    }
    body.appendChild(metaRow);

    // Row 3: Genre Tags Separated by 4px Grey Dots (Figma Node 109:9670)
    if (data.genreText) {
      const genreTokens = String(data.genreText)
        .split(/[,•/]+/)
        .map((g) => g.trim())
        .filter(Boolean)
        .slice(0, 3);
      if (genreTokens.length > 0) {
        const genresRow = el('div', 'bgp-preview-popover__genres');
        genreTokens.forEach((token, idx) => {
          if (idx > 0) {
            const dot = el('span', 'bgp-preview-popover__genre-dot');
            dot.setAttribute('aria-hidden', 'true');
            genresRow.appendChild(dot);
          }
          genresRow.appendChild(el('span', '', token));
        });
        body.appendChild(genresRow);
      }
    }

    const synopsisText =
      data.shortSynopsis || 'Nội dung tóm tắt của tác phẩm hiện đang được cập nhật.';
    body.appendChild(el('p', 'bgp-preview-popover__synopsis', synopsisText));

    // Row 4: Quick UTC+7 Showtime Pills -> /showtimes/{id}/seats
    const pillsWrap = el('div', 'bgp-time-pills');
    pillsWrap.setAttribute('data-testid', 'quick-preview-showtimes');
    const chips = Array.isArray(data.nextShowtimes) ? data.nextShowtimes.slice(0, 3) : [];

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
