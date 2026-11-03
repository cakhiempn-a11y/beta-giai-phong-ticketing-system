/**
 * Beta Giải Phóng — Netflix-Cinema Auditorium Seat Map Module (UI-P4)
 * Features:
 *  - WAI-ARIA 2D Roving Tabindex keyboard navigation (ArrowUp/Down/Left/Right, Home, End, Space/Enter)
 *  - Multi-cue state & glyph synchronization (• Standard, ★ VIP, ✓ Selected)
 *  - Max-8 seat guard with inline aria-live banner (zero window.alert)
 *  - One-shot Guest -> Login -> Customer draft seat restoration via sessionStorage (10-min TTL)
 *  - Mobile auditorium scroll-edge shadows & Fit-to-Screen zoom toggle
 */

const MAX_SEATS = 8;
const DRAFT_TTL_MS = 10 * 60 * 1000;

function formatVnd(amount) {
  return Number(amount || 0).toLocaleString('vi-VN') + ' đ';
}

function initAuditoriumSeatMap() {
  const pageRoot = document.querySelector('[data-seat-page]');
  if (!pageRoot) return;

  const showtimeId = pageRoot.getAttribute('data-showtime-id') || '';
  const isOpen = pageRoot.getAttribute('data-is-open') === 'true';
  const isOperationalLocked = pageRoot.getAttribute('data-operational-locked') === 'true';
  const authMode = pageRoot.getAttribute('data-auth-mode') || 'guest'; // 'customer' | 'guest' | 'operational'
  const draftStorageKey = `bgp_login_draft_seats_${showtimeId}`;

  const viewportWrap = pageRoot.querySelector('[data-auditorium-viewport-wrap]');
  const viewport = pageRoot.querySelector('[data-auditorium-viewport]');
  const gridContainer = pageRoot.querySelector('.seat-grid-container');
  const zoomToggleBtn = pageRoot.querySelector('[data-testid="seat-zoom-toggle"]');

  const badgesContainer = document.getElementById('selectedSeatsBadges');
  const countEl = document.getElementById('selectedCount');
  const totalEl = document.getElementById('selectedTotal');
  const csvInput = document.getElementById('seatIdsCsv');
  const submitBtn = document.getElementById('btnSubmit');
  const form = document.getElementById('formReserve');
  const clearAllBtn = pageRoot.querySelector('[data-seat-clear-all]');

  const limitBanner = pageRoot.querySelector('[data-testid="seat-limit-banner"]');
  const limitBannerText = pageRoot.querySelector('[data-seat-limit-text]');
  const draftRestoredBanner = pageRoot.querySelector('[data-testid="seat-draft-restored-banner"]');
  const operationalBanner = pageRoot.querySelector('[data-testid="seat-operational-lock-notice"]');

  const summaryPanel = pageRoot.querySelector('[data-summary-panel]');
  const mobileDock = pageRoot.querySelector('[data-testid="seat-mobile-dock"]');
  const mobileCountEl = pageRoot.querySelector('[data-mobile-seat-count]');
  const mobileTotalEl = pageRoot.querySelector('[data-mobile-seat-total]');
  const mobileSubmitProxy = pageRoot.querySelector('[data-mobile-submit-proxy]');

  // Wire alert dismiss buttons (Zero Bootstrap JS)
  document.querySelectorAll('[data-dismiss-alert]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const alertBox = btn.closest('.bgp-seat-alert');
      if (alertBox) {
        alertBox.hidden = true;
      }
    });
  });

  // Ensure header/drawer Login links preserve returnUrl to this seat map for Guests
  if (authMode === 'guest' && showtimeId) {
    const returnPath = `/showtimes/${showtimeId}/seats`;
    document.querySelectorAll('a[href*="/account/login"]').forEach((link) => {
      try {
        const url = new URL(link.getAttribute('href') || '/account/login', window.location.origin);
        if (!url.searchParams.has('returnUrl')) {
          url.searchParams.set('returnUrl', returnPath);
          link.setAttribute('href', url.pathname + url.search);
        }
      } catch {
        // Ignore malformed href
      }
      link.addEventListener('click', () => {
        saveGuestDraftSeats();
      });
    });
  }

  // Horizontal scroll shadow sync for narrow viewports (320px - 390px)
  function syncScrollShadows() {
    if (!viewport || !viewportWrap) return;
    const maxScrollLeft = viewport.scrollWidth - viewport.clientWidth;
    viewportWrap.classList.toggle('has-scroll-left', viewport.scrollLeft > 4);
    viewportWrap.classList.toggle('has-scroll-right', maxScrollLeft - viewport.scrollLeft > 4);
  }

  if (viewport) {
    viewport.addEventListener('scroll', syncScrollShadows, { passive: true });
    window.addEventListener('resize', syncScrollShadows, { passive: true });
    requestAnimationFrame(syncScrollShadows);
  }

  if (zoomToggleBtn && viewport) {
    zoomToggleBtn.addEventListener('click', () => {
      const nextFit = !viewport.classList.contains('is-fit-mode');
      viewport.classList.toggle('is-fit-mode', nextFit);
      zoomToggleBtn.setAttribute('aria-pressed', String(nextFit));
      const labelSpan = zoomToggleBtn.querySelector('[data-zoom-label]');
      if (labelSpan) {
        labelSpan.textContent = nextFit ? 'Kích thước 100%' : 'Thu nhỏ vừa màn hình';
      }
      requestAnimationFrame(syncScrollShadows);
    });
  }

  if (!gridContainer) return;

  // Build 2D Auditorium Matrix for Roving Tabindex keyboard navigation
  const rowEls = Array.from(gridContainer.querySelectorAll('.seat-row'));
  const matrix = rowEls
    .map((rowEl) => Array.from(rowEl.querySelectorAll('.seat-box')))
    .filter((row) => row.length > 0);

  if (matrix.length === 0) return;

  let activeR = 0;
  let activeC = 0;
  let foundInitialBookable = false;

  matrix.forEach((row, r) => {
    row.forEach((btn, c) => {
      btn.dataset.gridR = String(r);
      btn.dataset.gridC = String(c);
      if (!foundInitialBookable && btn.getAttribute('data-bookable') === 'true') {
        activeR = r;
        activeC = c;
        foundInitialBookable = true;
      }
      btn.setAttribute('tabindex', '-1');
    });
  });

  matrix[activeR][activeC].setAttribute('tabindex', '0');

  function setRovingFocus(nextR, nextC, focusDom = true) {
    const prevBtn = matrix[activeR]?.[activeC];
    const nextBtn = matrix[nextR]?.[nextC];
    if (!nextBtn) return;

    if (prevBtn && prevBtn !== nextBtn) {
      prevBtn.setAttribute('tabindex', '-1');
    }
    nextBtn.setAttribute('tabindex', '0');
    activeR = nextR;
    activeC = nextC;

    if (focusDom) {
      nextBtn.focus({ preventScroll: false });
      nextBtn.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }
  }

  gridContainer.addEventListener('focusin', (e) => {
    const btn = e.target instanceof Element ? e.target.closest('.seat-box') : null;
    if (!btn) return;
    const r = Number(btn.dataset.gridR ?? 0);
    const c = Number(btn.dataset.gridC ?? 0);
    setRovingFocus(r, c, false);
  });

  gridContainer.addEventListener('keydown', (e) => {
    const btn = e.target instanceof Element ? e.target.closest('.seat-box') : null;
    if (!btn) return;

    const r = Number(btn.dataset.gridR ?? activeR);
    const c = Number(btn.dataset.gridC ?? activeC);
    const currentRow = matrix[r] || [];

    let targetR = r;
    let targetC = c;

    switch (e.key) {
      case 'ArrowRight':
        targetC = Math.min(c + 1, currentRow.length - 1);
        break;
      case 'ArrowLeft':
        targetC = Math.max(c - 1, 0);
        break;
      case 'ArrowDown':
        targetR = Math.min(r + 1, matrix.length - 1);
        targetC = Math.min(c, matrix[targetR].length - 1);
        break;
      case 'ArrowUp':
        targetR = Math.max(r - 1, 0);
        targetC = Math.min(c, matrix[targetR].length - 1);
        break;
      case 'Home':
        if (e.ctrlKey) {
          targetR = 0;
          targetC = 0;
        } else {
          targetC = 0;
        }
        break;
      case 'End':
        if (e.ctrlKey) {
          targetR = matrix.length - 1;
          targetC = matrix[targetR].length - 1;
        } else {
          targetC = currentRow.length - 1;
        }
        break;
      default:
        return;
    }

    e.preventDefault();
    setRovingFocus(targetR, targetC, true);
  });

  // Selection state & UI synchronization
  const selectedSeats = new Map();
  let isSummaryVisibleInViewport = false;

  function syncMobileDockVisibility() {
    if (!mobileDock) return;
    const shouldShow = isOpen && !isOperationalLocked && selectedSeats.size > 0 && !isSummaryVisibleInViewport;
    mobileDock.hidden = !shouldShow;
    pageRoot.classList.toggle('has-mobile-dock', shouldShow);
  }

  if (summaryPanel && mobileDock && 'IntersectionObserver' in window) {
    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          isSummaryVisibleInViewport = entry.isIntersecting;
        }
        syncMobileDockVisibility();
      },
      { threshold: 0.25 }
    );
    observer.observe(summaryPanel);
  }

  function applySeatVisualState(btn, isSelected) {
    const seatCode = btn.getAttribute('data-seat-code') || '';
    const seatType = btn.getAttribute('data-seat-type') || '';
    const seatTypeCode = btn.getAttribute('data-seat-type-code') || 'STANDARD';
    const price = parseInt(btn.getAttribute('data-price') || '0', 10) || 0;
    const cueEl = btn.querySelector('[data-seat-cue]');

    btn.classList.toggle('seat-selected', isSelected);
    btn.classList.toggle('bgp-seat--selected', isSelected);
    btn.setAttribute('aria-pressed', isSelected ? 'true' : 'false');

    if (cueEl) {
      if (isSelected) {
        cueEl.textContent = '✓';
      } else {
        cueEl.textContent = seatTypeCode === 'VIP' ? '★' : '•';
      }
    }

    const statePhrase = isSelected ? 'đang chọn' : 'còn trống';
    btn.setAttribute('aria-label', `Ghế ${seatCode}, ${seatType}, ${formatVnd(price)}, ${statePhrase}`);
  }

  function hideLimitBanner() {
    if (limitBanner) {
      limitBanner.hidden = true;
    }
  }

  function showLimitBanner(attemptedCode) {
    if (!limitBanner) return;
    if (limitBannerText) {
      limitBannerText.textContent =
        `Mỗi đơn hàng hỗ trợ giữ tối đa ${MAX_SEATS} ghế (không thể thêm ghế ${attemptedCode}). ` +
        `Bỏ chọn một ghế đang giữ nếu bạn muốn đổi vị trí.`;
    }
    limitBanner.hidden = false;
  }

  function deselectSeatById(seatId) {
    const entry = selectedSeats.get(seatId);
    if (!entry) return;
    selectedSeats.delete(seatId);
    applySeatVisualState(entry.btn, false);
    hideLimitBanner();
    updateSelectionUI();
  }

  function clearAllSelectedSeats() {
    selectedSeats.forEach((entry) => {
      applySeatVisualState(entry.btn, false);
    });
    selectedSeats.clear();
    hideLimitBanner();
    updateSelectionUI();
  }

  function toggleSeatButton(btn) {
    if (!isOpen) return;

    if (isOperationalLocked) {
      if (operationalBanner) {
        operationalBanner.hidden = false;
        operationalBanner.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      }
      return;
    }

    if (btn.getAttribute('data-bookable') !== 'true') {
      return;
    }

    const seatId = parseInt(btn.getAttribute('data-seat-id') || '0', 10);
    if (!seatId) return;

    if (selectedSeats.has(seatId)) {
      deselectSeatById(seatId);
      return;
    }

    const seatCode = btn.getAttribute('data-seat-code') || '';
    if (selectedSeats.size >= MAX_SEATS) {
      showLimitBanner(seatCode);
      return;
    }

    const seatType = btn.getAttribute('data-seat-type') || 'Ghế tiêu chuẩn';
    const seatTypeCode = btn.getAttribute('data-seat-type-code') || 'STANDARD';
    const price = parseInt(btn.getAttribute('data-price') || '0', 10) || 0;

    selectedSeats.set(seatId, {
      id: seatId,
      code: seatCode,
      typeName: seatType,
      typeCode: seatTypeCode,
      price,
      btn,
    });
    applySeatVisualState(btn, true);
    hideLimitBanner();
    updateSelectionUI();
  }

  function updateSelectionUI() {
    const size = selectedSeats.size;
    if (countEl) countEl.textContent = String(size);
    if (clearAllBtn) clearAllBtn.hidden = size === 0;

    let total = 0;
    const ids = [];

    if (badgesContainer) {
      badgesContainer.innerHTML = '';

      if (size === 0) {
        const emptyEl = document.createElement('div');
        emptyEl.className = 'bgp-selected-empty';
        emptyEl.textContent = 'Chưa có ghế nào được chọn — Nhấp chuột hoặc dùng phím mũi tên + Space trên sơ đồ.';
        badgesContainer.appendChild(emptyEl);
      } else {
        selectedSeats.forEach((val, id) => {
          ids.push(id);
          total += val.price;

          const row = document.createElement('div');
          row.className = 'bgp-selected-item';

          const left = document.createElement('div');
          left.className = 'bgp-selected-item__left';

          const codeBadge = document.createElement('span');
          codeBadge.className = 'bgp-selected-item__code badge';
          codeBadge.textContent = val.code;

          const tierSpan = document.createElement('span');
          tierSpan.className = 'bgp-selected-item__tier';
          tierSpan.textContent = val.typeCode === 'VIP' ? `${val.typeName} ★` : val.typeName;

          left.appendChild(codeBadge);
          left.appendChild(tierSpan);

          const right = document.createElement('div');
          right.className = 'bgp-selected-item__right';

          const priceSpan = document.createElement('span');
          priceSpan.className = 'bgp-selected-item__price';
          priceSpan.textContent = formatVnd(val.price);

          const removeBtn = document.createElement('button');
          removeBtn.type = 'button';
          removeBtn.className = 'bgp-selected-item__remove';
          removeBtn.setAttribute('aria-label', `Bỏ chọn ghế ${val.code}`);
          removeBtn.textContent = '×';
          removeBtn.addEventListener('click', () => {
            deselectSeatById(id);
          });

          right.appendChild(priceSpan);
          right.appendChild(removeBtn);

          row.appendChild(left);
          row.appendChild(right);
          badgesContainer.appendChild(row);
        });
      }
    } else {
      selectedSeats.forEach((val, id) => {
        ids.push(id);
        total += val.price;
      });
    }

    const formattedTotal = formatVnd(total);
    if (totalEl) totalEl.textContent = formattedTotal;
    if (csvInput) csvInput.value = ids.join(',');

    if (submitBtn) {
      if (isOperationalLocked) {
        submitBtn.disabled = true;
      } else if (size === 0) {
        submitBtn.disabled = true;
        submitBtn.textContent =
          authMode === 'guest' ? 'Đăng nhập để giữ ghế →' : 'Giữ ghế & Tiếp tục thanh toán →';
      } else {
        submitBtn.disabled = false;
        submitBtn.textContent =
          authMode === 'guest'
            ? `Đăng nhập để giữ ${size} ghế →`
            : `Giữ ${size} ghế & Tiếp tục thanh toán →`;
      }
    }

    if (mobileCountEl) {
      const codesPreview = Array.from(selectedSeats.values())
        .map((s) => s.code)
        .join(', ');
      mobileCountEl.textContent = `${size}/${MAX_SEATS} ghế: ${codesPreview}`;
    }
    if (mobileTotalEl) {
      mobileTotalEl.textContent = formattedTotal;
    }
    if (mobileSubmitProxy) {
      mobileSubmitProxy.textContent = authMode === 'guest' ? 'Đăng nhập →' : 'Tiếp tục →';
    }

    syncMobileDockVisibility();
  }

  function saveGuestDraftSeats() {
    if (authMode !== 'guest' || !showtimeId) return;
    try {
      if (selectedSeats.size === 0) {
        sessionStorage.removeItem(draftStorageKey);
        return;
      }
      const payload = {
        seatIds: Array.from(selectedSeats.keys()),
        savedAt: Date.now(),
      };
      sessionStorage.setItem(draftStorageKey, JSON.stringify(payload));
    } catch {
      // Ignore storage quota / privacy errors
    }
  }

  function consumeAndRestoreCustomerDraftSeats() {
    if (authMode !== 'customer' || !isOpen || isOperationalLocked || !showtimeId) return;
    try {
      const raw = sessionStorage.getItem(draftStorageKey);
      if (!raw) return;
      sessionStorage.removeItem(draftStorageKey);

      const parsed = JSON.parse(raw);
      if (!parsed || !Array.isArray(parsed.seatIds) || typeof parsed.savedAt !== 'number') return;
      if (Date.now() - parsed.savedAt > DRAFT_TTL_MS) return;

      let restoredCount = 0;
      for (const id of parsed.seatIds) {
        if (selectedSeats.size >= MAX_SEATS) break;
        const btn = gridContainer.querySelector(`.seat-box[data-seat-id="${Number(id)}"][data-bookable="true"]`);
        if (btn) {
          toggleSeatButton(btn);
          restoredCount++;
        }
      }

      if (restoredCount > 0 && draftRestoredBanner) {
        draftRestoredBanner.hidden = false;
      }
    } catch {
      // Ignore malformed sessionStorage payload
    }
  }

  // Delegate seat clicks on auditorium container (handles Mouse, Touch, and native <button> Space/Enter)
  gridContainer.addEventListener('click', (e) => {
    const btn = e.target instanceof Element ? e.target.closest('.seat-box') : null;
    if (!btn) return;
    toggleSeatButton(btn);
  });

  if (clearAllBtn) {
    clearAllBtn.addEventListener('click', () => {
      clearAllSelectedSeats();
    });
  }

  if (mobileSubmitProxy && form) {
    mobileSubmitProxy.addEventListener('click', () => {
      if (typeof form.requestSubmit === 'function') {
        form.requestSubmit();
      } else {
        form.submit();
      }
    });
  }

  if (form) {
    form.addEventListener('submit', () => {
      if (authMode === 'guest') {
        saveGuestDraftSeats();
      } else if (showtimeId) {
        try {
          sessionStorage.removeItem(draftStorageKey);
        } catch {
          // Ignore
        }
      }

      if (submitBtn) {
        submitBtn.disabled = true;
        submitBtn.textContent = authMode === 'guest' ? 'Đang chuyển tới Đăng nhập...' : 'Đang khóa giữ ghế...';
      }
    });
  }

  updateSelectionUI();
  consumeAndRestoreCustomerDraftSeats();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initAuditoriumSeatMap);
} else {
  initAuditoriumSeatMap();
}
