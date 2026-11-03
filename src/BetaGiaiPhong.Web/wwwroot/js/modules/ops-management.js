/**
 * Beta Giải Phóng — Operations Management & External Provenance Sync (UI-P7)
 * Pure ES Module — Zero jQuery, Zero Bootstrap JS, Zero window.confirm/alert.
 * Handles:
 *  1. Dismissible operational flash banners ([data-dismiss-alert])
 *  2. Accessible HTML5 <dialog> confirmation modals ([data-confirm-dialog])
 *  3. Controlled External Movie Lookup & Sync ([data-external-lookup-root])
 *  4. Automatic ManualOverrideMask lock toggling on MovieEdit ([data-movie-edit-form])
 */

(function initOperationsManagement() {
  /* -----------------------------------------------------------------------
   * 1. Dismissible Flash Banners
   * ----------------------------------------------------------------------- */
  document.querySelectorAll("[data-dismiss-alert]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const alertBox = btn.closest(".bgp-ops-alert");
      if (alertBox) {
        alertBox.remove();
      }
    });
  });

  /* -----------------------------------------------------------------------
   * 2. Accessible HTML5 <dialog> Confirmation Modals (Archive, Close, Cancel)
   * ----------------------------------------------------------------------- */
  document.querySelectorAll("form[data-confirm-dialog]").forEach((form) => {
    const dialogId = form.getAttribute("data-confirm-dialog");
    if (!dialogId) return;

    const dialog = document.getElementById(dialogId);
    if (!dialog || typeof dialog.showModal !== "function") return;

    form.addEventListener("submit", (evt) => {
      if (form.dataset.dialogConfirmed === "true") {
        form.dataset.dialogConfirmed = "";
        return;
      }

      evt.preventDefault();
      dialog._pendingForm = form;
      dialog.showModal();

      const cancelBtn = dialog.querySelector("[data-dialog-cancel]");
      if (cancelBtn && typeof cancelBtn.focus === "function") {
        window.requestAnimationFrame(() => cancelBtn.focus());
      }
    });

    if (!dialog.dataset.opsDialogWired) {
      dialog.dataset.opsDialogWired = "true";

      dialog.querySelectorAll("[data-dialog-cancel]").forEach((cancelBtn) => {
        cancelBtn.addEventListener("click", () => {
          dialog._pendingForm = null;
          dialog.close();
        });
      });

      const confirmBtn = dialog.querySelector("[data-dialog-confirm]");
      if (confirmBtn) {
        confirmBtn.addEventListener("click", () => {
          const targetForm = dialog._pendingForm;
          dialog._pendingForm = null;
          dialog.close();
          if (targetForm) {
            targetForm.dataset.dialogConfirmed = "true";
            if (typeof targetForm.requestSubmit === "function") {
              targetForm.requestSubmit();
            } else {
              targetForm.submit();
            }
          }
        });
      }

      dialog.addEventListener("click", (evt) => {
        if (evt.target === dialog) {
          dialog._pendingForm = null;
          dialog.close();
        }
      });
    }
  });

  /* -----------------------------------------------------------------------
   * 3. External Provenance Lookup & Sync Drawer ([data-external-lookup-root])
   * ----------------------------------------------------------------------- */
  function getAntiForgeryToken() {
    const input = document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : "";
  }

  document.querySelectorAll("[data-toggle-sync-panel]").forEach((toggleBtn) => {
    const targetId = toggleBtn.getAttribute("data-toggle-sync-panel");
    if (!targetId) return;
    const panel = document.getElementById(targetId);
    if (!panel) return;

    toggleBtn.addEventListener("click", () => {
      const willOpen = panel.hasAttribute("hidden");
      if (willOpen) {
        panel.removeAttribute("hidden");
        toggleBtn.setAttribute("aria-expanded", "true");
        const searchInput = panel.querySelector("[data-sync-search-input]");
        if (searchInput && typeof searchInput.focus === "function") {
          window.requestAnimationFrame(() => searchInput.focus());
        }
      } else {
        panel.setAttribute("hidden", "");
        toggleBtn.setAttribute("aria-expanded", "false");
      }
    });
  });

  document.querySelectorAll("[data-external-lookup-root]").forEach((root) => {
    const mode = root.getAttribute("data-lookup-mode") || "catalog";
    const autofillSelector = root.getAttribute("data-autofill-form");
    const searchInput = root.querySelector("[data-sync-search-input]");
    const searchBtn = root.querySelector("[data-sync-search-btn]");
    const statusBox = root.querySelector("[data-sync-status]");
    const resultsBox = root.querySelector("[data-sync-results]");

    if (!searchInput || !searchBtn || !statusBox || !resultsBox) return;

    const setStatus = (message, tone = "neutral") => {
      statusBox.className = "bgp-sync-status";
      if (tone === "warning") statusBox.classList.add("bgp-sync-status--warning");
      if (tone === "error") statusBox.classList.add("bgp-sync-status--error");
      statusBox.textContent = message;
      statusBox.removeAttribute("hidden");
    };

    const applyAutofillToForm = (item) => {
      const form = autofillSelector ? document.querySelector(autofillSelector) : null;
      if (!form) return;

      const setField = (name, val) => {
        const el = form.querySelector(`[name="${name}"]`);
        if (!el) return;
        el.value = val !== null && val !== undefined ? String(val) : "";
      };

      setField("Title", item.title || "");
      setField("OriginalTitle", item.originalTitle || "");
      setField("ReleaseYear", item.releaseYear || "");
      if (item.durationMin && item.durationMin > 0) {
        setField("DurationMin", item.durationMin);
      }
      setField("GenreText", item.genreText || "");
      setField("DirectorNames", item.directorNames || "");
      setField("OriginCountry", item.originCountry || "");
      setField("OriginalLanguage", item.originalLanguage || "");
      setField("WikidataQid", item.wikidataQid || "");
      setField("ImdbId", item.imdbId || "");
      setField("TmdbId", item.tmdbId || "");
      setField("PosterUrl", item.posterUrl || item.posterW342Url || "");
      setField("BackdropUrl", item.backdropUrl || "");
      setField("OfficialTrailerYoutubeId", item.officialTrailerYoutubeId || "");
      setField("Synopsis", item.synopsis || "");

      // Never auto-convert foreign ratings to VN age labels; only populate if VN label is verified
      setField("AgeLabel", item.ageLabelVn || "");

      const detailsSection = form.querySelector("[data-provenance-details]");
      if (detailsSection) {
        detailsSection.setAttribute("open", "");
      }

      const ageHint = item.ageLabelVn
        ? `Nhãn độ tuổi VN xác minh: ${item.ageLabelVn}.`
        : "Nhãn độ tuổi VN được giữ trống (Chưa phân loại) — vui lòng chọn P/K/T13/T16/T18 theo giấy phép phổ biến phim.";

      setStatus(
        `Đã điền dữ liệu xác minh của "${item.title}" vào biểu mẫu bên dưới. ${ageHint}`,
        "neutral"
      );

      const titleInput = form.querySelector('[name="Title"]');
      if (titleInput && typeof titleInput.focus === "function") {
        titleInput.focus();
      }
    };

    const createProvenanceChip = (text) => {
      const span = document.createElement("span");
      span.className = "bgp-ops-id-chip";
      span.textContent = text;
      return span;
    };

    const renderResults = (items) => {
      resultsBox.innerHTML = "";
      if (!Array.isArray(items) || items.length === 0) {
        return;
      }

      const csrfToken = getAntiForgeryToken();

      items.forEach((item) => {
        const card = document.createElement("article");
        card.className = "bgp-sync-item";
        card.setAttribute("data-testid", "external-lookup-item");

        const top = document.createElement("div");
        top.className = "bgp-sync-item__top";

        const thumb = document.createElement("div");
        thumb.className = "bgp-ops-thumb";
        const posterSrc = item.posterW342Url || item.posterUrl;
        if (posterSrc) {
          const img = document.createElement("img");
          img.src = posterSrc;
          img.alt = item.title || "Poster";
          img.loading = "lazy";
          img.addEventListener("error", () => {
            img.remove();
            const fb = document.createElement("span");
            fb.className = "bgp-ops-thumb__fallback";
            fb.textContent = "NO IMG";
            thumb.appendChild(fb);
          });
          thumb.appendChild(img);
        } else {
          const fb = document.createElement("span");
          fb.className = "bgp-ops-thumb__fallback";
          fb.textContent = "NO IMG";
          thumb.appendChild(fb);
        }
        top.appendChild(thumb);

        const body = document.createElement("div");
        body.className = "bgp-sync-item__body";

        const titleEl = document.createElement("h4");
        titleEl.className = "bgp-sync-item__title";
        const yearSuffix = item.releaseYear ? ` (${item.releaseYear})` : "";
        titleEl.textContent = `${item.title || "Không rõ tựa"}${yearSuffix}`;
        body.appendChild(titleEl);

        const metaParts = [];
        if (item.originalTitle && item.originalTitle !== item.title) {
          metaParts.push(item.originalTitle);
        }
        if (item.durationMin > 0) {
          metaParts.push(`${item.durationMin} phút`);
        }
        if (item.directorNames) {
          metaParts.push(`Đạo diễn: ${item.directorNames}`);
        }
        if (item.genreText) {
          metaParts.push(item.genreText);
        }
        const metaEl = document.createElement("div");
        metaEl.className = "bgp-sync-item__meta";
        metaEl.textContent = metaParts.join(" • ");
        body.appendChild(metaEl);

        const chipList = document.createElement("div");
        chipList.className = "bgp-ops-id-list";
        if (item.wikidataQid) chipList.appendChild(createProvenanceChip(item.wikidataQid));
        if (item.imdbId) chipList.appendChild(createProvenanceChip(item.imdbId));
        if (item.tmdbId) chipList.appendChild(createProvenanceChip(`TMDB #${item.tmdbId}`));
        if (item.ratingValue !== null && item.ratingValue !== undefined) {
          const srcLabel = item.ratingSource === "TMDB" ? "TMDB" : "IMDb";
          chipList.appendChild(
            createProvenanceChip(`${srcLabel} ${Number(item.ratingValue).toFixed(1)}`)
          );
        }
        if (item.artworkSource) {
          chipList.appendChild(createProvenanceChip(item.artworkSource));
        }
        body.appendChild(chipList);

        if (item.synopsis) {
          const syn = document.createElement("p");
          syn.className = "bgp-sync-item__synopsis";
          syn.textContent = item.synopsis;
          body.appendChild(syn);
        }

        top.appendChild(body);
        card.appendChild(top);

        const foot = document.createElement("div");
        foot.className = "bgp-sync-item__foot";

        const stateBadge = document.createElement("span");
        if (item.existingMovieId) {
          stateBadge.className = "badge bgp-ops-badge bgp-ops-badge--emerald";
          stateBadge.textContent = `Đã có trong DB (#${item.existingMovieId} • ${item.existingStatus || "DRAFT"})`;
        } else {
          stateBadge.className = "badge bgp-ops-badge bgp-ops-badge--slate";
          stateBadge.textContent =
            item.sourceKind === "TMDB_LIVE"
              ? "Nguồn trực tiếp TMDB API"
              : "Danh mục xác minh Wikidata/IMDb";
        }
        foot.appendChild(stateBadge);

        const actionGroup = document.createElement("div");
        actionGroup.style.display = "flex";
        actionGroup.style.flexWrap = "wrap";
        actionGroup.style.gap = "8px";

        if (mode === "create") {
          const fillBtn = document.createElement("button");
          fillBtn.type = "button";
          fillBtn.className = "bgp-btn bgp-btn--primary bgp-btn--sm";
          fillBtn.setAttribute("data-testid", "autofill-verified-btn");
          fillBtn.textContent = "Điền vào biểu mẫu";
          fillBtn.addEventListener("click", () => applyAutofillToForm(item));
          actionGroup.appendChild(fillBtn);

          if (item.existingMovieId) {
            const viewLink = document.createElement("a");
            viewLink.href = `/management/movies/${item.existingMovieId}`;
            viewLink.className = "bgp-btn bgp-btn--outline bgp-btn--sm";
            viewLink.textContent = `Mở #${item.existingMovieId}`;
            actionGroup.appendChild(viewLink);
          }
        } else {
          const syncForm = document.createElement("form");
          syncForm.method = "post";
          syncForm.action = "/management/movies/sync-external";
          syncForm.style.margin = "0";

          const tokenInput = document.createElement("input");
          tokenInput.type = "hidden";
          tokenInput.name = "__RequestVerificationToken";
          tokenInput.value = csrfToken;
          syncForm.appendChild(tokenInput);

          if (item.verifiedKey) {
            const keyInput = document.createElement("input");
            keyInput.type = "hidden";
            keyInput.name = "VerifiedKey";
            keyInput.value = item.verifiedKey;
            syncForm.appendChild(keyInput);
          }
          if (item.tmdbId) {
            const tmdbInput = document.createElement("input");
            tmdbInput.type = "hidden";
            tmdbInput.name = "TmdbId";
            tmdbInput.value = String(item.tmdbId);
            syncForm.appendChild(tmdbInput);
          }
          if (item.existingMovieId) {
            const targetInput = document.createElement("input");
            targetInput.type = "hidden";
            targetInput.name = "TargetMovieId";
            targetInput.value = String(item.existingMovieId);
            syncForm.appendChild(targetInput);
          }

          const submitBtn = document.createElement("button");
          submitBtn.type = "submit";
          submitBtn.className = item.existingMovieId
            ? "bgp-btn bgp-btn--outline bgp-btn--sm"
            : "bgp-btn bgp-btn--primary bgp-btn--sm";
          submitBtn.setAttribute(
            "data-testid",
            item.existingMovieId ? "resync-external-btn" : "import-external-draft-btn"
          );
          submitBtn.textContent = item.existingMovieId
            ? `Đồng bộ lại (#${item.existingMovieId})`
            : "Nhập bản nháp (DRAFT)";
          syncForm.appendChild(submitBtn);
          actionGroup.appendChild(syncForm);

          if (item.existingMovieId) {
            const detailLink = document.createElement("a");
            detailLink.href = `/management/movies/${item.existingMovieId}`;
            detailLink.className = "bgp-btn bgp-btn--glass bgp-btn--sm";
            detailLink.textContent = "Chi tiết";
            actionGroup.appendChild(detailLink);
          }
        }

        foot.appendChild(actionGroup);
        card.appendChild(foot);
        resultsBox.appendChild(card);
      });
    };

    const executeLookup = async (rawQuery) => {
      const q = (rawQuery !== undefined ? rawQuery : searchInput.value || "").trim();
      searchBtn.disabled = true;
      setStatus("Đang truy vấn nguồn dữ liệu xác minh...", "neutral");

      try {
        const resp = await fetch(
          `/api/management/movies/external-lookup?query=${encodeURIComponent(q)}`,
          {
            method: "GET",
            headers: { Accept: "application/json" }
          }
        );

        if (!resp.ok) {
          setStatus(
            `Không thể tra cứu dữ liệu (HTTP ${resp.status}). Vui lòng kiểm tra phiên đăng nhập quản trị.`,
            "error"
          );
          resultsBox.innerHTML = "";
          return;
        }

        const payload = await resp.json();
        const tone =
          payload.statusCode === "TMDB_TOKEN_MISSING"
            ? "warning"
            : payload.statusCode === "EXTERNAL_ERROR"
              ? "error"
              : "neutral";

        setStatus(payload.statusMessage || "Hoàn tất tra cứu.", tone);
        renderResults(payload.items || []);
      } catch {
        setStatus("Lỗi kết nối mạng khi tra cứu nguồn dữ liệu phim.", "error");
        resultsBox.innerHTML = "";
      } finally {
        searchBtn.disabled = false;
      }
    };

    searchBtn.addEventListener("click", () => executeLookup());
    searchInput.addEventListener("keydown", (evt) => {
      if (evt.key === "Enter") {
        evt.preventDefault();
        executeLookup();
      }
    });

    root.querySelectorAll("[data-sync-quick-query]").forEach((chipBtn) => {
      chipBtn.addEventListener("click", () => {
        const q = chipBtn.getAttribute("data-sync-quick-query") || "";
        searchInput.value = q;
        executeLookup(q);
      });
    });
  });

  /* -----------------------------------------------------------------------
   * 4. Auto-Check ManualOverrideMask Lock When Manager Edits a Field
   * ----------------------------------------------------------------------- */
  const editForm = document.querySelector("[data-movie-edit-form]");
  if (editForm) {
    editForm.querySelectorAll("[data-override-field]").forEach((control) => {
      const fieldKey = control.getAttribute("data-override-field");
      if (!fieldKey) return;
      const checkbox = editForm.querySelector(
        `input[type="checkbox"][name="ManualOverrides"][value="${fieldKey}"]`
      );
      if (!checkbox) return;

      const markLocked = () => {
        checkbox.checked = true;
      };

      control.addEventListener("input", markLocked);
      control.addEventListener("change", markLocked);
    });
  }
})();
