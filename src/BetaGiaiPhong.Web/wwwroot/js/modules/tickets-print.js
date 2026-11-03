/**
 * BETA GIẢI PHÓNG — NETFLIX-CINEMA DESIGN SYSTEM
 * tickets-print.js: Print Boarding Passes & Alert Dismissal Controller
 */

(function initTicketsAndOrdersPage() {
  'use strict';

  document.querySelectorAll('[data-print-tickets]').forEach((btn) => {
    btn.addEventListener('click', () => {
      window.print();
    });
  });

  document.querySelectorAll('[data-dismiss-alert]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const box = btn.closest('.bgp-tk-alert');
      if (box instanceof HTMLElement) {
        box.hidden = true;
      }
    });
  });
})();
