/**
 * BETA GIẢI PHÓNG — NETFLIX-CINEMA DESIGN SYSTEM
 * account-login.js: Zero-jQuery Accessible Form Validation
 *                   & Development Demo Account Quick-Picker
 */

(function initAccountLogin() {
  'use strict';

  const form = document.getElementById('bgp-login-form');
  if (!(form instanceof HTMLFormElement)) return;

  const emailInput = form.querySelector('input[name="Request.Email"]');
  const passwordInput = form.querySelector('input[name="Request.Password"]');
  const emailMsg = form.querySelector('[data-valmsg-for="Request.Email"]');
  const passwordMsg = form.querySelector('[data-valmsg-for="Request.Password"]');

  function setFieldError(inputEl, msgEl, message) {
    if (!(inputEl instanceof HTMLElement) || !(msgEl instanceof HTMLElement)) return;
    if (message) {
      msgEl.textContent = message;
      msgEl.classList.remove('field-validation-valid');
      msgEl.classList.add('field-validation-error');
      inputEl.setAttribute('aria-invalid', 'true');
    } else {
      msgEl.textContent = '';
      msgEl.classList.remove('field-validation-error');
      msgEl.classList.add('field-validation-valid');
      inputEl.removeAttribute('aria-invalid');
    }
  }

  function validateEmail() {
    if (!(emailInput instanceof HTMLInputElement)) return true;
    const val = emailInput.value.trim();
    if (!val) {
      setFieldError(emailInput, emailMsg, 'Vui lòng nhập địa chỉ email.');
      return false;
    }
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(val)) {
      setFieldError(emailInput, emailMsg, 'Định dạng email không hợp lệ.');
      return false;
    }
    setFieldError(emailInput, emailMsg, '');
    return true;
  }

  function validatePassword() {
    if (!(passwordInput instanceof HTMLInputElement)) return true;
    const val = passwordInput.value;
    if (!val) {
      setFieldError(passwordInput, passwordMsg, 'Vui lòng nhập mật khẩu.');
      return false;
    }
    setFieldError(passwordInput, passwordMsg, '');
    return true;
  }

  if (emailInput instanceof HTMLInputElement) {
    emailInput.addEventListener('input', () => {
      if (emailInput.getAttribute('aria-invalid') === 'true') {
        validateEmail();
      }
    });
  }

  if (passwordInput instanceof HTMLInputElement) {
    passwordInput.addEventListener('input', () => {
      if (passwordInput.getAttribute('aria-invalid') === 'true') {
        validatePassword();
      }
    });
  }

  form.addEventListener('submit', (event) => {
    const emailOk = validateEmail();
    const passOk = validatePassword();
    if (!emailOk || !passOk) {
      event.preventDefault();
      if (!emailOk && emailInput instanceof HTMLElement) {
        emailInput.focus();
      } else if (!passOk && passwordInput instanceof HTMLElement) {
        passwordInput.focus();
      }
    }
  });

  // Development-only demo account email quick-picker (fills email & focuses password)
  const demoChips = document.querySelectorAll('[data-demo-email]');
  demoChips.forEach((chip) => {
    chip.addEventListener('click', () => {
      const targetEmail = chip.getAttribute('data-demo-email');
      if (!targetEmail || !(emailInput instanceof HTMLInputElement)) return;

      emailInput.value = targetEmail;
      setFieldError(emailInput, emailMsg, '');

      demoChips.forEach((c) => c.classList.remove('is-selected'));
      chip.classList.add('is-selected');

      if (passwordInput instanceof HTMLElement) {
        passwordInput.focus();
      }
    });
  });
})();
