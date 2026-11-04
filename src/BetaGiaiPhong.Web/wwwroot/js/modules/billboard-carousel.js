/**
 * Beta Giải Phóng — Netflix Billboard Carousel & Inline Highlight Video (UI-Billboard)
 * Pure ES Module — Zero external library dependencies.
 *
 * Capabilities:
 *  1. Smooth GPU-accelerated horizontal slide track (translate3d) across featured films
 *  2. Prev / Next controls, Netflix progress-bar dash indicators, keyboard arrows & touch swipe
 *  3. 8-second auto-advance with smart pause on hover, focus, active video, modal dialog,
 *     hidden browser tab, or prefers-reduced-motion: reduce
 *  4. Inline Full-Bleed Highlight Video stage (local MP4/WebM or privacy-enhanced youtube-nocookie)
 *     with Mute/Unmute control, 3s Netflix Compact Title transition, and instant DOM teardown on slide change
 *  5. Strict single-active-slide accessibility & test-hook synchronization (inert + aria-hidden)
 */

const VALID_YOUTUBE_ID_RE = /^[A-Za-z0-9_-]{6,20}$/;
const AUTO_ADVANCE_MS = 8000;
const COMPACT_VIDEO_DELAY_MS = 3200;
const SESSION_VIDEO_PREF_KEY = 'bgp_billboard_video_pref';
const SWIPE_THRESHOLD_PX = 44;

export function initBillboardCarousel() {
  const billboard = document.querySelector('[data-billboard]');
  if (!billboard) return;

  const viewport = billboard.querySelector('[data-billboard-viewport]');
  const track = billboard.querySelector('[data-billboard-track]');
  const slides = Array.from(billboard.querySelectorAll('[data-billboard-slide]'));
  if (!viewport || !track || slides.length === 0) return;

  const prevBtn = billboard.querySelector('[data-billboard-prev]');
  const nextBtn = billboard.querySelector('[data-billboard-next]');
  const indicators = Array.from(billboard.querySelectorAll('[data-billboard-indicator]'));
  const trailerPillBtn = billboard.querySelector('[data-billboard-trailer-pill]');
  const videoToggleBtn = billboard.querySelector('[data-billboard-video-toggle]');
  const muteToggleBtn = billboard.querySelector('[data-billboard-mute-toggle]');
  const edgeAgeEl = billboard.querySelector('[data-billboard-edge-age]');
  const liveRegion = billboard.querySelector('[data-billboard-live]');
  const trailerDialog = document.getElementById('bgp-trailer-dialog');

  const reducedMotionQuery = window.matchMedia
    ? window.matchMedia('(prefers-reduced-motion: reduce)')
    : null;

  let activeIndex = 0;
  let isHovered = false;
  let isFocusWithin = false;
  let isInlineVideoPlaying = false;
  let isMuted = true;
  let sessionWantsVideo = readSessionVideoPref();
  /** @type {number | null} */
  let autoTimerId = null;
  /** @type {number | null} */
  let compactTimerId = null;
  /** @type {number | null} */
  let settleVideoTimerId = null;

  billboard.style.setProperty('--bgp-billboard-interval', `${AUTO_ADVANCE_MS}ms`);

  function readSessionVideoPref() {
    try {
      return window.sessionStorage.getItem(SESSION_VIDEO_PREF_KEY) === 'on';
    } catch {
      return false;
    }
  }

  function writeSessionVideoPref(enabled) {
    try {
      window.sessionStorage.setItem(SESSION_VIDEO_PREF_KEY, enabled ? 'on' : 'off');
    } catch {
      // Ignore storage restrictions
    }
  }

  function prefersReducedMotion() {
    return Boolean(reducedMotionQuery && reducedMotionQuery.matches);
  }

  function isModalOpen() {
    return Boolean(trailerDialog && trailerDialog.hasAttribute('open'));
  }

  function slideHasVideoSource(slide) {
    if (!slide) return false;
    const directUrl = (slide.getAttribute('data-direct-video-url') || '').trim();
    const ytId = (slide.getAttribute('data-trailer-youtube-id') || '').trim();
    return directUrl.length > 0 || VALID_YOUTUBE_ID_RE.test(ytId);
  }

  function clearCompactTimer() {
    if (compactTimerId !== null) {
      window.clearTimeout(compactTimerId);
      compactTimerId = null;
    }
  }

  function clearSettleVideoTimer() {
    if (settleVideoTimerId !== null) {
      window.clearTimeout(settleVideoTimerId);
      settleVideoTimerId = null;
    }
  }

  function teardownSlideVideo(slide) {
    if (!slide) return;
    slide.classList.remove('is-video-playing', 'is-video-compact');
    const stage = slide.querySelector('[data-billboard-video-stage]');
    if (!stage) return;

    const iframe = stage.querySelector('iframe');
    if (iframe) {
      iframe.src = 'about:blank';
    }
    const video = stage.querySelector('video');
    if (video) {
      try {
        video.pause();
        video.removeAttribute('src');
        video.load();
      } catch {
        // Ignore media cleanup errors
      }
    }
    stage.replaceChildren();
  }

  function teardownAllVideos() {
    clearCompactTimer();
    clearSettleVideoTimer();
    slides.forEach((s) => teardownSlideVideo(s));
    isInlineVideoPlaying = false;
    syncMediaDockButtons();
  }

  function syncMediaDockButtons() {
    const currentSlide = slides[activeIndex];
    const hasVideo = slideHasVideoSource(currentSlide);
    const ytId = currentSlide ? (currentSlide.getAttribute('data-trailer-youtube-id') || '').trim() : '';
    const movieTitle = currentSlide ? currentSlide.getAttribute('data-movie-title') || '' : '';

    if (trailerPillBtn) {
      const hasValidYt = VALID_YOUTUBE_ID_RE.test(ytId);
      trailerPillBtn.hidden = !hasValidYt;
      if (hasValidYt) {
        trailerPillBtn.setAttribute('data-trailer-open', ytId);
        trailerPillBtn.setAttribute('data-trailer-title', movieTitle);
        trailerPillBtn.setAttribute('aria-label', `Xem trailer chính thức phim ${movieTitle}`);
      } else {
        trailerPillBtn.removeAttribute('data-trailer-open');
      }
    }

    if (videoToggleBtn) {
      videoToggleBtn.hidden = !hasVideo;
      videoToggleBtn.setAttribute('aria-pressed', isInlineVideoPlaying ? 'true' : 'false');
      videoToggleBtn.setAttribute(
        'aria-label',
        isInlineVideoPlaying ? 'Chuyển về ảnh nền tĩnh' : 'Phát video nổi bật ngay trên nền'
      );
    }

    if (muteToggleBtn) {
      muteToggleBtn.hidden = !isInlineVideoPlaying;
      muteToggleBtn.setAttribute('aria-pressed', isMuted ? 'true' : 'false');
      muteToggleBtn.setAttribute(
        'aria-label',
        isMuted ? 'Bật âm thanh video nền' : 'Tắt âm thanh video nền'
      );
    }

    if (edgeAgeEl && currentSlide) {
      const ageLabel = (currentSlide.getAttribute('data-age-label') || '').trim();
      if (ageLabel) {
        edgeAgeEl.textContent = ageLabel;
        edgeAgeEl.hidden = false;
      } else {
        edgeAgeEl.textContent = '';
        edgeAgeEl.hidden = true;
      }
    }
  }

  function applyMuteStateToActivePlayer() {
    const currentSlide = slides[activeIndex];
    if (!currentSlide) return;
    const stage = currentSlide.querySelector('[data-billboard-video-stage]');
    if (!stage) return;

    const video = stage.querySelector('video');
    if (video) {
      video.muted = isMuted;
    }

    const iframe = stage.querySelector('iframe');
    if (iframe && iframe.contentWindow) {
      const func = isMuted ? 'mute' : 'unMute';
      iframe.contentWindow.postMessage(
        JSON.stringify({ event: 'command', func, args: [] }),
        '*'
      );
    }
  }

  function mountSlideVideo(slide) {
    if (!slide || !slideHasVideoSource(slide)) return;

    teardownAllVideos();
    const stage = slide.querySelector('[data-billboard-video-stage]');
    if (!stage) return;

    const directUrl = (slide.getAttribute('data-direct-video-url') || '').trim();
    const ytId = (slide.getAttribute('data-trailer-youtube-id') || '').trim();
    const movieTitle = slide.getAttribute('data-movie-title') || 'Phim nổi bật';

    if (directUrl.length > 0) {
      const video = document.createElement('video');
      video.src = directUrl;
      video.muted = isMuted;
      video.autoplay = true;
      video.loop = true;
      video.playsInline = true;
      video.setAttribute('playsinline', '');
      video.setAttribute('tabindex', '-1');
      video.setAttribute('aria-hidden', 'true');
      video.setAttribute('data-testid', 'billboard-inline-video');
      stage.appendChild(video);
      const playPromise = video.play();
      if (playPromise && typeof playPromise.catch === 'function') {
        playPromise.catch(() => {});
      }
    } else if (VALID_YOUTUBE_ID_RE.test(ytId)) {
      const encodedId = encodeURIComponent(ytId);
      const muteParam = isMuted ? '1' : '0';
      const iframe = document.createElement('iframe');
      iframe.src =
        `https://www.youtube-nocookie.com/embed/${encodedId}` +
        `?autoplay=1&mute=${muteParam}&controls=0&loop=1&playlist=${encodedId}` +
        `&playsinline=1&rel=0&modestbranding=1&iv_load_policy=3&disablekb=1&enablejsapi=1`;
      iframe.title = `Video nổi bật — ${movieTitle}`;
      iframe.allow = 'accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture';
      iframe.referrerPolicy = 'strict-origin-when-cross-origin';
      iframe.tabIndex = -1;
      iframe.setAttribute('aria-hidden', 'true');
      iframe.setAttribute('data-testid', 'billboard-inline-video');
      stage.appendChild(iframe);
    } else {
      return;
    }

    slide.classList.add('is-video-playing');
    isInlineVideoPlaying = true;
    syncMediaDockButtons();
    syncAutoAdvance();

    if (!prefersReducedMotion()) {
      compactTimerId = window.setTimeout(() => {
        if (isInlineVideoPlaying && slides[activeIndex] === slide) {
          slide.classList.add('is-video-compact');
        }
      }, COMPACT_VIDEO_DELAY_MS);
    }
  }

  function preloadNeighborSlideImage(index) {
    if (slides.length <= 1) return;
    const nextIdx = (index + 1) % slides.length;
    const neighborImg = slides[nextIdx]?.querySelector('[data-slide-backdrop]');
    if (neighborImg instanceof HTMLImageElement && neighborImg.loading === 'lazy') {
      neighborImg.loading = 'eager';
    }
  }

  function syncActiveSlideSemantics(nextIndex, announce) {
    slides.forEach((slide, idx) => {
      const isCurrent = idx === nextIndex;
      slide.classList.toggle('is-active', isCurrent);
      slide.setAttribute('aria-hidden', isCurrent ? 'false' : 'true');
      if ('inert' in slide) {
        slide.inert = !isCurrent;
      }
      if (isCurrent) {
        slide.removeAttribute('inert');
      } else {
        slide.setAttribute('inert', '');
      }

      const heading = slide.querySelector('[data-slide-heading]');
      if (heading) {
        heading.classList.toggle('bgp-hero-title', isCurrent);
        heading.classList.toggle('bgp-billboard-slide__title', !isCurrent);
        if (isCurrent) {
          heading.setAttribute('id', 'hero-title');
        } else {
          heading.removeAttribute('id');
        }
      }

      const eyebrow = slide.querySelector('[data-slide-eyebrow]');
      if (eyebrow) {
        eyebrow.classList.toggle('bgp-hero-eyebrow', isCurrent);
        eyebrow.classList.toggle('bgp-billboard-slide__eyebrow', !isCurrent);
      }

      const synopsis = slide.querySelector('[data-slide-synopsis]');
      if (synopsis) {
        synopsis.classList.toggle('bgp-hero-synopsis', isCurrent);
        synopsis.classList.toggle('bgp-billboard-slide__synopsis', !isCurrent);
      }

      const toggleTestId = (selector, testIdValue) => {
        const el = slide.querySelector(selector);
        if (!el) return;
        if (isCurrent) {
          el.setAttribute('data-testid', testIdValue);
        } else {
          el.removeAttribute('data-testid');
        }
      };

      toggleTestId('[data-slide-backdrop]', 'hero-backdrop-img');
      toggleTestId('[data-slide-rating]', 'hero-rating-badge');
      toggleTestId('[data-slide-age]', 'hero-age-badge');
      toggleTestId('[data-slide-quick-times]', 'hero-quick-showtimes');
      toggleTestId('[data-slide-primary-cta]', 'hero-primary-cta');
      toggleTestId('[data-slide-secondary-cta]', 'hero-secondary-cta');
      toggleTestId('[data-slide-trailer-btn]', 'hero-trailer-btn');
    });

    indicators.forEach((btn, idx) => {
      const isCurrent = idx === nextIndex;
      btn.classList.toggle('is-active', isCurrent);
      btn.setAttribute('aria-selected', isCurrent ? 'true' : 'false');
    });

    if (announce && liveRegion) {
      const title = slides[nextIndex]?.getAttribute('data-movie-title') || '';
      liveRegion.textContent = `Đang hiển thị phim nổi bật ${nextIndex + 1} / ${slides.length}: ${title}`;
    }
  }

  function restartProgressAnimation() {
    billboard.classList.remove('is-autoplay-running');
    // Force reflow so CSS keyframe restarts cleanly on active dash indicator
    void billboard.offsetWidth;
    if (canAutoAdvance()) {
      billboard.classList.add('is-autoplay-running');
    }
  }

  function canAutoAdvance() {
    return (
      slides.length > 1 &&
      !prefersReducedMotion() &&
      !isHovered &&
      !isFocusWithin &&
      !isInlineVideoPlaying &&
      !isModalOpen() &&
      document.visibilityState !== 'hidden'
    );
  }

  function stopAutoAdvance() {
    if (autoTimerId !== null) {
      window.clearTimeout(autoTimerId);
      autoTimerId = null;
    }
    billboard.classList.toggle('is-autoplay-paused', !canAutoAdvance());
  }

  function syncAutoAdvance() {
    stopAutoAdvance();
    if (!canAutoAdvance()) {
      billboard.classList.remove('is-autoplay-running');
      return;
    }
    billboard.classList.remove('is-autoplay-paused');
    billboard.classList.add('is-autoplay-running');
    autoTimerId = window.setTimeout(() => {
      goToSlide((activeIndex + 1) % slides.length, { announce: false });
    }, AUTO_ADVANCE_MS);
  }

  function goToSlide(targetIndex, options = { announce: true }) {
    if (slides.length === 0) return;
    const normalized = ((targetIndex % slides.length) + slides.length) % slides.length;
    const changed = normalized !== activeIndex;

    if (changed) {
      teardownAllVideos();
    }

    activeIndex = normalized;
    track.style.transform =
      activeIndex === 0 ? 'translate3d(0%, 0, 0)' : `translate3d(-${activeIndex * 100}%, 0, 0)`;

    syncActiveSlideSemantics(activeIndex, Boolean(options.announce && changed));
    syncMediaDockButtons();
    preloadNeighborSlideImage(activeIndex);
    restartProgressAnimation();
    syncAutoAdvance();

    if (changed && sessionWantsVideo && !prefersReducedMotion() && slideHasVideoSource(slides[activeIndex])) {
      settleVideoTimerId = window.setTimeout(() => {
        if (slides[activeIndex] && !isModalOpen()) {
          mountSlideVideo(slides[activeIndex]);
        }
      }, 620);
    }
  }

  // 1. Wire Prev / Next & Indicator Tabs
  if (prevBtn) {
    prevBtn.addEventListener('click', () => {
      goToSlide(activeIndex - 1, { announce: true });
    });
  }

  if (nextBtn) {
    nextBtn.addEventListener('click', () => {
      goToSlide(activeIndex + 1, { announce: true });
    });
  }

  indicators.forEach((btn, idx) => {
    btn.addEventListener('click', () => {
      if (idx !== activeIndex) {
        goToSlide(idx, { announce: true });
      }
    });
  });

  // 2. Wire Inline Highlight Video & Mute Controls
  if (videoToggleBtn) {
    videoToggleBtn.addEventListener('click', () => {
      const currentSlide = slides[activeIndex];
      if (!currentSlide) return;

      if (isInlineVideoPlaying) {
        sessionWantsVideo = false;
        writeSessionVideoPref(false);
        teardownAllVideos();
        syncAutoAdvance();
      } else {
        sessionWantsVideo = true;
        writeSessionVideoPref(true);
        mountSlideVideo(currentSlide);
      }
    });
  }

  if (muteToggleBtn) {
    muteToggleBtn.addEventListener('click', () => {
      isMuted = !isMuted;
      syncMediaDockButtons();
      applyMuteStateToActivePlayer();
    });
  }

  // 3. Keyboard Navigation (ArrowLeft / ArrowRight inside Billboard)
  billboard.addEventListener('keydown', (evt) => {
    if (slides.length <= 1) return;
    if (evt.altKey || evt.ctrlKey || evt.metaKey) return;

    if (evt.key === 'ArrowLeft') {
      evt.preventDefault();
      goToSlide(activeIndex - 1, { announce: true });
    } else if (evt.key === 'ArrowRight') {
      evt.preventDefault();
      goToSlide(activeIndex + 1, { announce: true });
    }
  });

  // 4. Touch / Pointer Horizontal Swipe Gesture
  let pointerStartX = 0;
  let pointerStartY = 0;
  let pointerTracking = false;

  viewport.addEventListener(
    'pointerdown',
    (evt) => {
      if (slides.length <= 1) return;
      if (evt.pointerType === 'mouse' && evt.button !== 0) return;
      const interactive =
        evt.target instanceof Element ? evt.target.closest('a, button, input, select, textarea') : null;
      if (interactive) return;

      pointerStartX = evt.clientX;
      pointerStartY = evt.clientY;
      pointerTracking = true;
    },
    { passive: true }
  );

  viewport.addEventListener(
    'pointerup',
    (evt) => {
      if (!pointerTracking) return;
      pointerTracking = false;

      const dx = evt.clientX - pointerStartX;
      const dy = evt.clientY - pointerStartY;
      if (Math.abs(dx) >= SWIPE_THRESHOLD_PX && Math.abs(dx) > Math.abs(dy) * 1.35) {
        if (dx < 0) {
          goToSlide(activeIndex + 1, { announce: true });
        } else {
          goToSlide(activeIndex - 1, { announce: true });
        }
      }
    },
    { passive: true }
  );

  viewport.addEventListener(
    'pointercancel',
    () => {
      pointerTracking = false;
    },
    { passive: true }
  );

  // 5. Smart Auto-Advance Pause / Resume Listeners
  billboard.addEventListener('mouseenter', () => {
    isHovered = true;
    syncAutoAdvance();
  });

  billboard.addEventListener('mouseleave', () => {
    isHovered = false;
    syncAutoAdvance();
  });

  billboard.addEventListener('focusin', () => {
    isFocusWithin = true;
    syncAutoAdvance();
  });

  billboard.addEventListener('focusout', (evt) => {
    const nextTarget = /** @type {Node | null} */ (evt.relatedTarget);
    if (!nextTarget || !billboard.contains(nextTarget)) {
      isFocusWithin = false;
      syncAutoAdvance();
    }
  });

  document.addEventListener('visibilitychange', () => {
    syncAutoAdvance();
  });

  if (reducedMotionQuery && typeof reducedMotionQuery.addEventListener === 'function') {
    reducedMotionQuery.addEventListener('change', () => {
      if (prefersReducedMotion()) {
        teardownAllVideos();
      }
      syncAutoAdvance();
    });
  }

  // Pause inline video cleanly if user opens the modal trailer <dialog>
  document.addEventListener('click', (evt) => {
    const trigger = evt.target instanceof Element ? evt.target.closest('[data-trailer-open]') : null;
    if (trigger && isInlineVideoPlaying) {
      teardownAllVideos();
    }
  });

  if (trailerDialog) {
    trailerDialog.addEventListener('close', () => {
      syncAutoAdvance();
    });
  }

  // Initial state setup
  preloadNeighborSlideImage(0);
  syncMediaDockButtons();
  syncAutoAdvance();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initBillboardCarousel);
} else {
  initBillboardCarousel();
}
