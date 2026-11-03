/**
 * Beta Giải Phóng — Horizontal Cinema Rail Carousel (UI-P3)
 * Pure ES Module — Zero jQuery / Zero Bootstrap JS dependencies.
 * Supports multiple rails per page ([data-rail-root] or .bgp-rail-wrapper),
 * boundary button disabling, ResizeObserver sync, keyboard ArrowLeft/Right,
 * and prefers-reduced-motion: reduce.
 */

function prefersReducedMotion() {
  return Boolean(
    window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches
  );
}

export function initRailCarousels() {
  const wrappers = document.querySelectorAll('[data-rail-root], .bgp-rail-wrapper');

  wrappers.forEach((wrapper) => {
    const scroller = wrapper.querySelector(
      '[data-rail-scroller], .bgp-movie-rail, .bgp-wide-rail'
    );
    const btnPrev = wrapper.querySelector('[data-rail-prev], .bgp-rail-btn--prev');
    const btnNext = wrapper.querySelector('[data-rail-next], .bgp-rail-btn--next');

    if (!scroller || !btnPrev || !btnNext) return;

    const syncButtons = () => {
      const maxScrollLeft = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
      const isScrollable = maxScrollLeft > 8;

      if (!isScrollable) {
        btnPrev.disabled = true;
        btnNext.disabled = true;
        wrapper.classList.add('bgp-rail-wrapper--static');
        return;
      }

      wrapper.classList.remove('bgp-rail-wrapper--static');
      btnPrev.disabled = scroller.scrollLeft <= 8;
      btnNext.disabled = scroller.scrollLeft >= maxScrollLeft - 8;
    };

    const scrollByStep = (direction) => {
      const step = Math.max(Math.round(scroller.clientWidth * 0.78), 220);
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
      ro.observe(wrapper);
    }

    syncButtons();
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initRailCarousels);
} else {
  initRailCarousels();
}
