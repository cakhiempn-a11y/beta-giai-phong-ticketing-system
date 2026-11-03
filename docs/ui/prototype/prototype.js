// Beta Giải Phóng — Standalone Prototype Interactive Behaviors
// Progressive Enhancement ES Script (Header Scroll, Date Tabs, Seat Toggle, Countdown)

document.addEventListener('DOMContentLoaded', () => {
  const header = document.querySelector('.bgp-header[data-scroll-elevate]');
  if (header) {
    const syncHeader = () => {
      header.classList.toggle('bgp-header--solid', window.scrollY > 32);
    };
    window.addEventListener('scroll', syncHeader, { passive: true });
    syncHeader();
  }

  // Interactive Seat Buttons (2D Keyboard + Click)
  const seatButtons = document.querySelectorAll('button.bgp-seat:not([disabled])');
  seatButtons.forEach((btn) => {
    btn.addEventListener('click', () => {
      const pressed = btn.getAttribute('aria-pressed') === 'true';
      btn.setAttribute('aria-pressed', String(!pressed));
      btn.classList.toggle('bgp-seat--selected', !pressed);
    });
  });
});
