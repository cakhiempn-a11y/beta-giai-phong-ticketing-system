/**
 * Beta Giải Phóng — Movie Catalogue Instant Select Filter Bar (RapPhim Browse Pattern)
 * Progressive enhancement over GET <form>: auto-navigates cleanly on <select> change
 * while stripping default/empty query parameters so URLs stay minimal and shareable.
 */
(() => {
  const form = document.querySelector('[data-catalog-filter-form]');
  if (!(form instanceof HTMLFormElement)) return;

  /**
   * Builds a clean GET URL preserving only active non-default parameters.
   * Resets pagination to page 1 whenever filters or search terms change.
   * @param {HTMLFormElement} targetForm
   * @returns {string}
   */
  function buildCleanCatalogUrl(targetForm) {
    const actionAttr = targetForm.getAttribute('action') || '/movies';
    const url = new URL(actionAttr, window.location.origin);
    const fd = new FormData(targetForm);

    for (const [key, rawVal] of fd.entries()) {
      const val = String(rawVal).trim();
      if (!val) continue;
      if (key === 'filter' && val.toLowerCase() === 'all') continue;
      if (key === 'sort' && val.toLowerCase() === 'default') continue;
      if (key === 'page') continue;
      if (key === 'pageSize' && val === '24') continue;
      url.searchParams.set(key, val);
    }

    return `${url.pathname}${url.search}`;
  }

  const selects = form.querySelectorAll('select[data-catalog-auto-submit]');
  for (const select of selects) {
    select.addEventListener('change', () => {
      const targetUrl = buildCleanCatalogUrl(form);
      window.location.assign(targetUrl);
    });
  }

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    const targetUrl = buildCleanCatalogUrl(form);
    window.location.assign(targetUrl);
  });
})();
