/**
 * Beta Giải Phóng — Horizontal Cinema Rail Carousel & Header Page Counter (UI-P3 / RapPhim Style)
 * Pure ES Module — Zero jQuery / Zero Bootstrap JS dependencies.
 * Supports multiple rails per page ([data-rail-root] or .bgp-rail-wrapper),
 * dynamic header page indicator (1/N), boundary button disabling, ResizeObserver sync,
 * keyboard ArrowLeft/Right, and prefers-reduced-motion: reduce.
 */

function prefersReducedMotion() {
  return Boolean(
    window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches
  );
}

export function initRailCarousels() {
  const roots = document.querySelectorAll(
    '[data-rail-root], .bgp-rail-wrapper:not([data-rail-root] .bgp-rail-wrapper)'
  );

  roots.forEach((root) => {
    const scroller = root.querySelector(
      '[data-rail-scroller], .bgp-movie-rail, .bgp-wide-rail, .bgp-skew-rail'
    );
    const btnPrev = root.querySelector('[data-rail-prev]');
    const btnNext = root.querySelector('[data-rail-next]');
    const pageBadge = root.querySelector('[data-rail-page]');

    if (!scroller || !btnPrev || !btnNext) return;

    const getStepWidth = () => Math.max(Math.round(scroller.clientWidth * 0.82), 240);

    const syncButtons = () => {
      const maxScrollLeft = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
      const isScrollable = maxScrollLeft > 8;

      if (!isScrollable) {
        btnPrev.disabled = true;
        btnNext.disabled = true;
        root.classList.add('bgp-rail-wrapper--static');
        if (pageBadge) {
          pageBadge.textContent = '1/1';
          pageBadge.hidden = true;
        }
        return;
      }

      root.classList.remove('bgp-rail-wrapper--static');
      btnPrev.disabled = scroller.scrollLeft <= 8;
      btnNext.disabled = scroller.scrollLeft >= maxScrollLeft - 8;

      if (pageBadge) {
        const step = getStepWidth();
        const totalPages = Math.max(2, Math.ceil(maxScrollLeft / step) + 1);
        const rawPage =
          scroller.scrollLeft >= maxScrollLeft - 8
            ? totalPages
            : Math.min(totalPages, Math.floor((scroller.scrollLeft + step * 0.35) / step) + 1);
        pageBadge.textContent = `${rawPage}/${totalPages}`;
        pageBadge.hidden = false;
      }
    };

    const scrollByStep = (direction) => {
      const step = getStepWidth();
      scroller.scrollBy({
        left: direction * step,
        behavior: prefersReducedMotion() ? 'auto' : 'smooth',
      });
    };

    btnPrev.addEventListener('click', () => scrollByStep(-1));
    btnNext.addEventListener('click', () => scrollByStep(1));

    scroller.addEventListener('keydown', (evt) => {
      if (evt.key === 'ArrowRight') {
        evt.preventDefault();
        scrollByStep(1);
      } else if (evt.key === 'ArrowLeft') {
        evt.preventDefault();
        scrollByStep(-1);
      }
    });

    let rafId = 0;
    scroller.addEventListener(
      'scroll',
      () => {
        if (rafId) window.cancelAnimationFrame(rafId);
        rafId = window.requestAnimationFrame(syncButtons);
      },
      { passive: true }
    );

    window.addEventListener('resize', syncButtons, { passive: true });

    if (typeof ResizeObserver !== 'undefined') {
      const ro = new ResizeObserver(syncButtons);
      ro.observe(scroller);
      ro.observe(root);
    }

    syncButtons();
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initRailCarousels);
} else {
  initRailCarousels();
}
