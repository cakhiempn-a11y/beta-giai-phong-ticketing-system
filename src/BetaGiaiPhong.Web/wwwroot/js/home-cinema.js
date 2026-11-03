/**
 * Beta Giai Phong Cinema Ticketing System
 * UI-02A: Cinematic Homepage & Movie Rail Interactive Controls
 */
(() => {
    'use strict';

    function initMovieRails() {
        const wrappers = document.querySelectorAll('.bgp-rail-wrapper');
        const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        wrappers.forEach((wrapper) => {
            const rail = wrapper.querySelector('.bgp-movie-rail, [data-testid="movie-rail"]');
            const btnPrev = wrapper.querySelector('.bgp-rail-btn-prev, .bgp-rail-btn--prev, [data-testid="rail-btn-prev"]');
            const btnNext = wrapper.querySelector('.bgp-rail-btn-next, .bgp-rail-btn--next, [data-testid="rail-btn-next"]');

            if (!rail || !btnPrev || !btnNext) return;

            function updateButtons() {
                const maxScrollLeft = rail.scrollWidth - rail.clientWidth;
                const isScrollable = maxScrollLeft > 8;

                if (!isScrollable) {
                    btnPrev.disabled = true;
                    btnNext.disabled = true;
                    btnPrev.classList.add('is-hidden');
                    btnNext.classList.add('is-hidden');
                    btnPrev.setAttribute('aria-hidden', 'true');
                    btnNext.setAttribute('aria-hidden', 'true');
                    return;
                }

                btnPrev.classList.remove('is-hidden');
                btnNext.classList.remove('is-hidden');
                btnPrev.removeAttribute('aria-hidden');
                btnNext.removeAttribute('aria-hidden');

                btnPrev.disabled = rail.scrollLeft <= 8;
                btnNext.disabled = rail.scrollLeft >= maxScrollLeft - 8;
            }

            btnPrev.addEventListener('click', () => {
                const scrollAmount = Math.max(rail.clientWidth * 0.75, 200);
                rail.scrollBy({
                    left: -scrollAmount,
                    behavior: prefersReducedMotion ? 'auto' : 'smooth'
                });
            });

            btnNext.addEventListener('click', () => {
                const scrollAmount = Math.max(rail.clientWidth * 0.75, 200);
                rail.scrollBy({
                    left: scrollAmount,
                    behavior: prefersReducedMotion ? 'auto' : 'smooth'
                });
            });

            let scrollTimer = null;
            rail.addEventListener('scroll', () => {
                if (scrollTimer) cancelAnimationFrame(scrollTimer);
                scrollTimer = requestAnimationFrame(updateButtons);
            }, { passive: true });

            window.addEventListener('resize', updateButtons, { passive: true });

            if (typeof ResizeObserver !== 'undefined') {
                const ro = new ResizeObserver(() => {
                    updateButtons();
                });
                ro.observe(rail);
                ro.observe(wrapper);
            }

            // Initial button state calculation
            updateButtons();
        });
    }

    function initPosterFallbacks() {
        const fallbackPath = '/images/poster-fallback.svg';
        const posters = document.querySelectorAll('.bgp-card-poster, .bgp-hero-poster-img');

        posters.forEach((img) => {
            img.addEventListener('error', function onPosterError() {
                // Ensure this handler never triggers more than once per image
                img.removeEventListener('error', onPosterError);
                if (!img.src.endsWith(fallbackPath)) {
                    img.src = fallbackPath;
                }
            }, { once: true });
        });
    }

    function init() {
        initMovieRails();
        initPosterFallbacks();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
