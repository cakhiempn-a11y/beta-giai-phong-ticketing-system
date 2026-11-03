/**
 * BETA GIẢI PHÓNG — NETFLIX-CINEMA DESIGN SYSTEM
 * hold-countdown.js: Drift-Free Monotonic Hold Countdown (performance.now())
 *                    & Accessible <dialog> Action Confirmation Controller
 * Zero Bootstrap / Zero jQuery / Zero window.confirm() / Zero Client Clock Skew
 */

(function initHoldCountdownAndDialogs() {
  'use strict';

  function formatMmSs(totalSeconds) {
    const clamped = Math.max(0, Math.floor(totalSeconds));
    const minutes = Math.floor(clamped / 60);
    const seconds = clamped % 60;
    const mStr = minutes < 10 ? `0${minutes}` : String(minutes);
    const sStr = seconds < 10 ? `0${seconds}` : String(seconds);
    return `${mStr}:${sStr}`;
  }

  function setupAlertDismissal() {
    document.querySelectorAll('[data-dismiss-alert]').forEach((btn) => {
      btn.addEventListener('click', () => {
        const alertBox = btn.closest('.bgp-tx-alert');
        if (alertBox) {
          alertBox.hidden = true;
        }
      });
    });
  }

  function setupConfirmationDialogs() {
    let activeForm = null;
    let lastTriggerEl = null;

    const forms = document.querySelectorAll('form[data-confirm-dialog]');
    forms.forEach((form) => {
      form.addEventListener('submit', (event) => {
        if (form.dataset.confirmed === 'true') {
          return;
        }

        const dialogId = form.getAttribute('data-confirm-dialog');
        if (!dialogId) return;

        const dialog = document.getElementById(dialogId);
        if (!dialog) return;

        event.preventDefault();
        activeForm = form;
        lastTriggerEl = event.submitter || form.querySelector('button[type="submit"]');

        if (typeof dialog.showModal === 'function') {
          if (!dialog.open) {
            dialog.showModal();
          }
        } else {
          dialog.setAttribute('open', '');
        }

        const cancelBtn = dialog.querySelector('[data-dialog-cancel]');
        if (cancelBtn instanceof HTMLElement) {
          cancelBtn.focus();
        }
      });
    });

    document.querySelectorAll('dialog.bgp-confirm-dialog').forEach((dialog) => {
      const closeDialogCleanly = () => {
        if (typeof dialog.close === 'function' && dialog.open) {
          dialog.close();
        } else {
          dialog.removeAttribute('open');
        }
        if (lastTriggerEl instanceof HTMLElement && !lastTriggerEl.disabled) {
          lastTriggerEl.focus();
        }
      };

      dialog.querySelectorAll('[data-dialog-cancel]').forEach((btn) => {
        btn.addEventListener('click', () => {
          activeForm = null;
          closeDialogCleanly();
        });
      });

      // Close on backdrop click outside dialog box
      dialog.addEventListener('click', (event) => {
        if (event.target === dialog) {
          activeForm = null;
          closeDialogCleanly();
        }
      });

      dialog.addEventListener('close', () => {
        if (lastTriggerEl instanceof HTMLElement && !lastTriggerEl.disabled && !activeForm) {
          lastTriggerEl.focus();
        }
      });

      const confirmBtn = dialog.querySelector('[data-dialog-confirm]');
      if (confirmBtn) {
        confirmBtn.addEventListener('click', () => {
          if (!activeForm) {
            closeDialogCleanly();
            return;
          }

          const targetForm = activeForm;
          activeForm = null;
          targetForm.dataset.confirmed = 'true';

          if (confirmBtn instanceof HTMLButtonElement) {
            confirmBtn.disabled = true;
          }

          if (typeof dialog.close === 'function' && dialog.open) {
            dialog.close();
          } else {
            dialog.removeAttribute('open');
          }

          if (typeof targetForm.requestSubmit === 'function') {
            targetForm.requestSubmit();
          } else {
            targetForm.submit();
          }
        });
      }
    });
  }

  function setupMonotonicHoldCountdown() {
    const timerEl = document.getElementById('countdownTimer');
    if (!timerEl) return;

    const initialSeconds = Math.max(0, parseInt(timerEl.getAttribute('data-remaining') || '0', 10) || 0);
    const totalWindowSec = Math.max(initialSeconds, parseInt(timerEl.getAttribute('data-window-seconds') || '300', 10) || 300);
    const startMonotonicMs = performance.now();

    const bannerEl = document.querySelector('[data-hold-banner]');
    const progressEl = document.querySelector('[data-hold-progress]');
    const liveStatusEl = document.getElementById('bgp-hold-live-status');
    const expiredPanelEl = document.querySelector('[data-testid="hold-expired-inline-panel"]');

    let announcedCritical = false;
    let isTerminated = false;
    let intervalId = null;

    function lockInteractiveHoldControls() {
      const lockTargets = document.querySelectorAll(
        '#btnPaySuccess, #btnPayFailed, #btnProceedCheckout, #btnCancel, #btnCancelCheckout, [data-lock-on-expire]'
      );

      lockTargets.forEach((el) => {
        if (el instanceof HTMLButtonElement || el instanceof HTMLInputElement) {
          el.disabled = true;
          el.setAttribute('aria-disabled', 'true');
        } else if (el instanceof HTMLElement) {
          el.setAttribute('aria-disabled', 'true');
          el.classList.add('is-disabled');
          el.addEventListener('click', (evt) => evt.preventDefault());
        }
      });

      // Close any open confirmation dialogs if timer expired while modal was open
      document.querySelectorAll('dialog.bgp-confirm-dialog').forEach((dlg) => {
        if (typeof dlg.close === 'function' && dlg.open) {
          dlg.close();
        } else {
          dlg.removeAttribute('open');
        }
      });

      if (expiredPanelEl instanceof HTMLElement) {
        expiredPanelEl.hidden = false;
      }
    }

    function tick() {
      if (isTerminated) return;

      const elapsedSeconds = Math.floor((performance.now() - startMonotonicMs) / 1000);
      const remaining = Math.max(0, initialSeconds - elapsedSeconds);

      if (progressEl instanceof HTMLElement) {
        const pct = totalWindowSec > 0 ? Math.min(100, Math.max(0, (remaining / totalWindowSec) * 100)) : 0;
        progressEl.style.width = `${pct.toFixed(2)}%`;
      }

      if (remaining <= 0) {
        isTerminated = true;
        timerEl.textContent = '00:00 (Hết hạn)';
        timerEl.classList.add('bgp-hold-clock--expired');

        if (bannerEl) {
          bannerEl.classList.remove('is-critical');
          bannerEl.classList.add('is-expired');
          const statusPill = bannerEl.querySelector('.bgp-status-pill');
          if (statusPill) {
            statusPill.classList.remove('bgp-status-pill--pending');
            statusPill.classList.add('bgp-status-pill--expired');
            statusPill.textContent = 'Hết hạn giữ chỗ • 00:00';
          }
        }

        if (liveStatusEl) {
          liveStatusEl.textContent = 'Thời gian giữ ghế đã hết hạn (00:00). Các nút thanh toán đã tự động khóa, vui lòng chọn lại ghế cho suất chiếu.';
        }

        lockInteractiveHoldControls();

        if (intervalId !== null) {
          window.clearInterval(intervalId);
          intervalId = null;
        }
        return;
      }

      timerEl.textContent = formatMmSs(remaining);

      if (remaining <= 60) {
        if (bannerEl) {
          bannerEl.classList.add('is-critical');
        }
        if (!announcedCritical && liveStatusEl) {
          announcedCritical = true;
          liveStatusEl.textContent = `Còn dưới 1 phút giữ ghế (${formatMmSs(remaining)}). Vui lòng hoàn tất thanh toán ngay.`;
        }
      }
    }

    tick();

    if (!isTerminated) {
      intervalId = window.setInterval(tick, 250);
      document.addEventListener('visibilitychange', () => {
        if (!document.hidden) {
          tick();
        }
      });
    }
  }

  function preventDoubleSubmitOnDirectPay() {
    const paySuccessForm = document.getElementById('formPaySuccess');
    if (!paySuccessForm) return;

    paySuccessForm.addEventListener('submit', () => {
      const btn = document.getElementById('btnPaySuccess');
      if (btn instanceof HTMLButtonElement) {
        window.setTimeout(() => {
          btn.disabled = true;
        }, 0);
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
      setupAlertDismissal();
      setupConfirmationDialogs();
      setupMonotonicHoldCountdown();
      preventDoubleSubmitOnDirectPay();
    });
  } else {
    setupAlertDismissal();
    setupConfirmationDialogs();
    setupMonotonicHoldCountdown();
    preventDoubleSubmitOnDirectPay();
  }
})();
