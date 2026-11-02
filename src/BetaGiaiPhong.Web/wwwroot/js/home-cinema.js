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
            const rail = wrapper.querySelector('.bgp-movie-rail');
            const btnPrev = wrapper.querySelector('.bgp-rail-btn-prev');
            const btnNext = wrapper.querySelector('.bgp-rail-btn-next');

            if (!rail || !btnPrev || !btnNext) return;

            function updateButtons() {
                const maxScrollLeft = rail.scrollWidth - rail.clientWidth;
                const isScrollable = maxScrollLeft > 8;

                if (!isScrollable) {
                    btnPrev.disabled = true;
                    btnNext.disabled = true;
                    return;
                }

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

            // Initial button state calculation
            updateButtons();
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initMovieRails);
    } else {
        initMovieRails();
    }
})();
