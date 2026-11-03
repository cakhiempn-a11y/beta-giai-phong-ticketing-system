/**
 * Beta Giải Phóng — Dual-Shell Navigation & Header Behavior (UI-P1)
 * Pure ES Module — Zero jQuery / Zero Bootstrap JS dependencies.
 * Handles:
 *  1. Translucent -> Solid Frosted Obsidian Header on scroll (scrollY > 32px)
 *  2. Inline Header Search (GET /movies?q=...) & Mobile Search Focus
 *  3. Accessible User Dropdown (<details>) outside-click & Escape dismissal
 *  4. Consumer Mobile Drawer & Operations Console Mobile Drawer (focus + Escape)
 */

(function initShellNavigation() {
  const SCROLL_THRESHOLD_PX = 32;
  const DESKTOP_BREAKPOINT_PX = 992;

  /* -----------------------------------------------------------------------
   * 1. Consumer Header Scroll Elevation
   * ----------------------------------------------------------------------- */
  const cinemaHeader = document.querySelector('[data-cinema-header]');
  if (cinemaHeader) {
    const syncHeaderScrollState = () => {
      const isScrolled = window.scrollY > SCROLL_THRESHOLD_PX;
      cinemaHeader.classList.toggle('bgp-cinema-header--scrolled', isScrolled);
    };

    syncHeaderScrollState();
    window.addEventListener('scroll', syncHeaderScrollState, { passive: true });
  }

  /* -----------------------------------------------------------------------
   * 2. Header Inline Search & Drawer Search (GET /movies?q=...)
   *    Uses type="button" triggers so header never hijacks generic
   *    button[type="submit"] selectors inside page <main> forms.
   * ----------------------------------------------------------------------- */
  function wireSearchForm(formSelector, triggerSelector, inputSelector) {
    const form = document.querySelector(formSelector);
    if (!form) return;

    const trigger = form.querySelector(triggerSelector);
    const input = form.querySelector(inputSelector);
    if (!trigger || !input) return;

    input.addEventListener('focus', () => {
      form.classList.add('is-expanded');
    });

    input.addEventListener('blur', () => {
      if (!input.value.trim()) {
        form.classList.remove('is-expanded');
      }
    });

    trigger.addEventListener('click', () => {
      const trimmed = input.value.trim();
      if (trimmed.length > 0 || document.activeElement === input) {
        if (typeof form.requestSubmit === 'function') {
          form.requestSubmit();
        } else {
          form.submit();
        }
      } else {
        form.classList.add('is-expanded');
        input.focus();
      }
    });
  }

  wireSearchForm('[data-header-search]', '[data-search-trigger]', '[data-search-input]');
  wireSearchForm('[data-drawer-search]', '[data-drawer-search-trigger]', '[data-drawer-search-input]');

  /* -----------------------------------------------------------------------
   * 3. Accessible User Menu (<details data-user-menu>)
   * ----------------------------------------------------------------------- */
  const userMenu = document.querySelector('[data-user-menu]');
  if (userMenu) {
    document.addEventListener('click', (evt) => {
      if (userMenu.hasAttribute('open') && !userMenu.contains(evt.target)) {
        userMenu.removeAttribute('open');
      }
    });

    userMenu.addEventListener('keydown', (evt) => {
      if (evt.key === 'Escape' && userMenu.hasAttribute('open')) {
        userMenu.removeAttribute('open');
        const summary = userMenu.querySelector('summary');
        if (summary) summary.focus();
      }
    });
  }

  /* -----------------------------------------------------------------------
   * 4. Slide-Over Mobile Drawer Helper (Consumer & Operations Shells)
   * ----------------------------------------------------------------------- */
  function wireMobileDrawer(options) {
    const drawer = document.querySelector(options.drawerSelector);
    const toggleBtn = document.querySelector(options.toggleSelector);
    const closeBtn = document.querySelector(options.closeSelector);
    const backdrop = document.querySelector(options.backdropSelector);

    if (!drawer || !toggleBtn) return null;

    const setOpen = (open, focusTargetSelector) => {
      drawer.classList.toggle('is-open', open);
      drawer.setAttribute('aria-hidden', open ? 'false' : 'true');
      toggleBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
      if (backdrop) {
        backdrop.classList.toggle('is-open', open);
      }
      document.body.style.overflow = open ? 'hidden' : '';

      if (open) {
        const target =
          (focusTargetSelector && drawer.querySelector(focusTargetSelector)) ||
          closeBtn ||
          drawer.querySelector('a, button, input');
        if (target && typeof target.focus === 'function') {
          window.requestAnimationFrame(() => target.focus());
        }
      }
    };

    toggleBtn.addEventListener('click', () => {
      const nextState = !drawer.classList.contains('is-open');
      setOpen(nextState);
    });

    if (closeBtn) {
      closeBtn.addEventListener('click', () => {
        setOpen(false);
        toggleBtn.focus();
      });
    }

    if (backdrop) {
      backdrop.addEventListener('click', () => {
        setOpen(false);
      });
    }

    document.addEventListener('keydown', (evt) => {
      if (evt.key === 'Escape' && drawer.classList.contains('is-open')) {
        setOpen(false);
        toggleBtn.focus();
      }
    });

    window.addEventListener('resize', () => {
      if (window.innerWidth >= DESKTOP_BREAKPOINT_PX && drawer.classList.contains('is-open')) {
        setOpen(false);
      }
    });

    return { setOpen };
  }

  const cinemaDrawerCtrl = wireMobileDrawer({
    drawerSelector: '[data-cinema-drawer]',
    toggleSelector: '[data-cinema-drawer-toggle]',
    closeSelector: '[data-cinema-drawer-close]',
    backdropSelector: '[data-cinema-drawer-backdrop]',
  });

  const mobileSearchBtn = document.querySelector('[data-cinema-mobile-search]');
  if (mobileSearchBtn && cinemaDrawerCtrl) {
    mobileSearchBtn.addEventListener('click', () => {
      cinemaDrawerCtrl.setOpen(true, '[data-drawer-search-input]');
    });
  }

  wireMobileDrawer({
    drawerSelector: '[data-ops-sidebar]',
    toggleSelector: '[data-ops-drawer-toggle]',
    closeSelector: '[data-ops-drawer-close]',
    backdropSelector: '[data-ops-backdrop]',
  });
})();
