/**
 * Beta Giải Phóng — Gate QR Ticket Check-In Console (UI-P7)
 * Pure ES Module — Zero jQuery, Zero Bootstrap JS, Zero window.alert/confirm.
 * Preserves:
 *  - Deterministic multi-stage ZXing canvas decoding on isolated #qrFileScratch
 *  - Strict CSRF (X-XSRF-TOKEN + RequestVerificationToken)
 *  - Safe DOM API rendering (createElement + textContent; zero raw token logging)
 *  - Inline camera error banner (#cameraErrorBanner) & optional Web Audio tone
 */

(function initGateQrScanner() {
  const btnStart = document.getElementById("btnStartCamera");
  const btnStop = document.getElementById("btnStopCamera");
  const btnToggleSound = document.getElementById("btnToggleSound");
  const placeholder = document.getElementById("camera-placeholder");
  const cameraErrorBanner = document.getElementById("cameraErrorBanner");
  const cameraErrorText = document.getElementById("cameraErrorText");
  const btnDismissCameraError = document.getElementById("btnDismissCameraError");
  const showtimeSelect = document.getElementById("showtimeSelect");
  const qrFileInput = document.getElementById("qrFileInput");
  const qrUploadFileName = document.getElementById("qrUploadFileName");
  const manualForm = document.getElementById("manualScanForm");
  const manualInput = document.getElementById("manualQrToken");
  const resultCard = document.getElementById("resultCard");
  const ticketDetailsCard = document.getElementById("ticketDetailsCard");
  const statusBadge = document.getElementById("scanStatusBadge");

  if (!resultCard || !statusBadge) {
    return;
  }

  let html5QrCode = null;
  let fileScannerInstance = null;
  let isScanning = false;
  let isInFlight = false;
  let isTicketAccepted = false;
  let lastScannedToken = "";
  let pendingRetryToken = null;
  let soundEnabled = false;
  let audioCtx = null;

  // Safe diagnostic telemetry (never logs raw QR tokens, hashes, keys, or image bytes)
  function emitScanTelemetry(stage, meta) {
    try {
      const safeMeta = meta ? JSON.stringify(meta) : "{}";
      console.info(`[QR-SCAN-STAGE] ${stage} ${safeMeta}`);
    } catch {
      // Ignore telemetry serialization errors
    }
  }

  function showCameraError(reasonText) {
    if (!cameraErrorBanner || !cameraErrorText) return;
    cameraErrorText.textContent =
      "Không thể khởi động camera trên thiết bị này: " +
      (reasonText || "Thiết bị không có camera hoặc chưa cấp quyền truy cập.");
    cameraErrorBanner.classList.remove("is-hidden");
  }

  function hideCameraError() {
    if (!cameraErrorBanner) return;
    cameraErrorBanner.classList.add("is-hidden");
  }

  if (btnDismissCameraError) {
    btnDismissCameraError.addEventListener("click", hideCameraError);
  }

  // Optional subtle Web Audio tone feedback (disabled by default; silent fallback)
  function playFeedbackTone(isAccepted) {
    if (!soundEnabled) return;
    try {
      const Ctx = window.AudioContext || window.webkitAudioContext;
      if (!Ctx) return;
      if (!audioCtx) {
        audioCtx = new Ctx();
      }
      if (audioCtx.state === "suspended") {
        audioCtx.resume().catch(() => {});
      }
      const osc = audioCtx.createOscillator();
      const gain = audioCtx.createGain();
      osc.type = isAccepted ? "sine" : "triangle";
      osc.frequency.setValueAtTime(isAccepted ? 880 : 240, audioCtx.currentTime);
      gain.gain.setValueAtTime(0.06, audioCtx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, audioCtx.currentTime + (isAccepted ? 0.14 : 0.22));
      osc.connect(gain);
      gain.connect(audioCtx.destination);
      osc.start();
      osc.stop(audioCtx.currentTime + (isAccepted ? 0.15 : 0.24));
    } catch {
      // Never block gate operations if Web Audio is unavailable
    }
  }

  if (btnToggleSound) {
    btnToggleSound.addEventListener("click", () => {
      soundEnabled = !soundEnabled;
      btnToggleSound.setAttribute("aria-pressed", soundEnabled ? "true" : "false");
      const labelSpan = btnToggleSound.querySelector("[data-sound-label]");
      if (labelSpan) {
        labelSpan.textContent = soundEnabled ? "Âm báo: Bật" : "Âm báo: Tắt";
      }
    });
  }

  // Initialize or retrieve dedicated QR-only file scanner with ZXing TRY_HARDER enabled
  function getOrCreateFileScanner() {
    if (!fileScannerInstance) {
      const formats =
        typeof Html5QrcodeSupportedFormats !== "undefined" &&
        Html5QrcodeSupportedFormats.QR_CODE !== undefined
          ? [Html5QrcodeSupportedFormats.QR_CODE]
          : undefined;
      fileScannerInstance = new Html5Qrcode("qrFileScratch", {
        verbose: false,
        formatsToSupport: formats,
        useBarCodeDetectorIfSupported: false
      });
      // Enable ZXing DecodeHintType.TRY_HARDER (enum key 3) on both primary and secondary decoders
      try {
        if (
          fileScannerInstance.qrcode &&
          fileScannerInstance.qrcode.primaryDecoder &&
          fileScannerInstance.qrcode.primaryDecoder.hints
        ) {
          fileScannerInstance.qrcode.primaryDecoder.hints.set(3, true);
        }
        if (
          fileScannerInstance.qrcode &&
          fileScannerInstance.qrcode.secondaryDecoder &&
          fileScannerInstance.qrcode.secondaryDecoder.hints
        ) {
          fileScannerInstance.qrcode.secondaryDecoder.hints.set(3, true);
        }
      } catch {
        // Ignore if internal hints map structure differs
      }
      emitScanTelemetry("scanner_initialized", { container: "qrFileScratch", tryHarder: true });
    }
    return fileScannerInstance;
  }

  function getAntiForgeryToken() {
    const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    return tokenInput ? tokenInput.value : "";
  }

  function getCookie(name) {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop().split(";").shift();
    return null;
  }

  function renderIdleResultState() {
    resultCard.innerHTML = "";
    const idleBox = document.createElement("div");
    idleBox.className = "bgp-gate-result-box";

    const iconWrap = document.createElement("div");
    iconWrap.style.marginBottom = "10px";
    iconWrap.style.color = "var(--bgp-text-muted)";
    iconWrap.innerHTML = `<svg width="52" height="52" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <rect x="3" y="3" width="7" height="7"></rect>
      <rect x="14" y="3" width="7" height="7"></rect>
      <rect x="3" y="14" width="7" height="7"></rect>
      <path d="M14 14h3v3h-3zM18 18h3v3h-3zM14 19h2M19 14v2"></path>
    </svg>`;
    idleBox.appendChild(iconWrap);

    const heading = document.createElement("h3");
    heading.className = "bgp-gate-result__heading";
    heading.style.fontSize = "1.05rem";
    heading.textContent = "Chưa có lượt quét nào";
    idleBox.appendChild(heading);

    const desc = document.createElement("p");
    desc.className = "bgp-gate-result__msg";
    desc.style.marginBottom = "0";
    desc.textContent = "Hãy chọn đúng suất chiếu và quét mã QR của khách hàng để kiểm tra vé hợp lệ.";
    idleBox.appendChild(desc);

    resultCard.appendChild(idleBox);
  }

  // Reset UI state for scanning next ticket
  function resetScanState(focusInput = false) {
    isTicketAccepted = false;
    lastScannedToken = "";
    pendingRetryToken = null;
    isInFlight = false;

    statusBadge.className = "badge bgp-ops-badge bgp-ops-badge--slate";
    statusBadge.textContent = "Sẵn sàng quét";

    if (ticketDetailsCard) {
      ticketDetailsCard.classList.add("is-hidden");
    }
    renderIdleResultState();

    if (manualInput) {
      manualInput.value = "";
      if (focusInput) {
        manualInput.focus();
      }
    }
    if (qrUploadFileName) {
      qrUploadFileName.textContent = "Hỗ trợ PNG, JPG, WEBP từ vé điện tử";
    }
    emitScanTelemetry("ui_state_reset", { badge: "Sẵn sàng quét" });
  }

  // Submit check-in scan with in-flight guard and duplicate-camera-frame protection
  async function submitScan(qrToken, isExplicitRescan = false) {
    if (isInFlight) return;

    if (!qrToken || typeof qrToken !== "string") {
      renderResult({
        outcome: "INVALID_QR_IMAGE",
        isSuccess: false,
        message: "Không tìm thấy chuỗi mã QR hợp lệ để kiểm tra."
      });
      return;
    }
    qrToken = qrToken.trim();
    if (qrToken.length === 0) {
      renderResult({
        outcome: "INVALID_QR_IMAGE",
        isSuccess: false,
        message: "Chuỗi mã QR trống hoặc không hợp lệ."
      });
      return;
    }

    if (qrToken.length > 256) {
      renderResult({
        outcome: "ERROR",
        isSuccess: false,
        message: "Mã QR không đúng định dạng quy chuẩn (vượt quá 256 ký tự)."
      });
      return;
    }

    if (isTicketAccepted && !isExplicitRescan) {
      return;
    }

    isInFlight = true;
    lastScannedToken = qrToken;
    pendingRetryToken = qrToken;

    const showtimeId = parseInt(showtimeSelect ? showtimeSelect.value : "0", 10);
    if (!showtimeId || showtimeId <= 0) {
      isInFlight = false;
      renderResult({
        outcome: "ERROR",
        isSuccess: false,
        message: "Vui lòng chọn một suất chiếu hợp lệ trước khi soát vé."
      });
      return;
    }

    statusBadge.className = "badge bgp-ops-badge bgp-ops-badge--info";
    statusBadge.textContent = "Đang kiểm tra...";
    emitScanTelemetry("api_post_started", {
      expectedShowtimeId: showtimeId,
      tokenLength: qrToken.length,
      isExplicitRescan: !!isExplicitRescan
    });

    try {
      const xsrfCookie = getCookie("XSRF-TOKEN");
      const token = xsrfCookie || getAntiForgeryToken();
      const headers = {
        "Content-Type": "application/json",
        "X-XSRF-TOKEN": token,
        RequestVerificationToken: token
      };

      const response = await fetch("/api/staff/tickets/scan", {
        method: "POST",
        headers: headers,
        body: JSON.stringify({
          qrToken: qrToken,
          expectedShowtimeId: showtimeId
        })
      });

      const data = await response.json();
      emitScanTelemetry("api_post_completed", {
        status: response.status,
        outcome: data ? data.outcome : "UNKNOWN"
      });
      renderResult(data);
    } catch {
      emitScanTelemetry("api_post_failed", { reason: "network_error" });
      renderResult({
        outcome: "NETWORK_ERROR",
        isSuccess: false,
        message: "Không thể kết nối đến máy chủ để xác thực vé. Vui lòng kiểm tra mạng và thử lại."
      });
    } finally {
      isInFlight = false;
    }
  }

  // Safe DOM rendering (never interpolates server/user strings into innerHTML)
  function renderResult(data) {
    const outcome = (data && data.outcome) || "UNKNOWN";
    let boxModifier = "bgp-gate-result-box--danger";
    let badgeModifier = "bgp-ops-badge--danger";
    let badgeText = outcome;
    let iconSvg = "";

    switch (outcome) {
      case "ACCEPTED":
        isTicketAccepted = true;
        boxModifier = "bgp-gate-result-box--accepted";
        badgeModifier = "bgp-ops-badge--emerald";
        badgeText = "HỢP LỆ";
        iconSvg = `<svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#10B981" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><path d="m9 12 2 2 4-4"></path></svg>`;
        playFeedbackTone(true);
        break;
      case "ALREADY_USED":
        isTicketAccepted = false;
        boxModifier = "bgp-gate-result-box--warning";
        badgeModifier = "bgp-ops-badge--amber";
        badgeText = "ĐÃ SỬ DỤNG";
        iconSvg = `<svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#FBBF24" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>`;
        playFeedbackTone(false);
        break;
      case "WRONG_SHOWTIME":
        isTicketAccepted = false;
        boxModifier = "bgp-gate-result-box--warning";
        badgeModifier = "bgp-ops-badge--amber";
        badgeText = "SAI SUẤT CHIẾU";
        iconSvg = `<svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#FBBF24" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>`;
        playFeedbackTone(false);
        break;
      case "OUTSIDE_WINDOW":
        isTicketAccepted = false;
        boxModifier = "bgp-gate-result-box--neutral";
        badgeModifier = "bgp-ops-badge--slate";
        badgeText = "NGOÀI GIỜ SOÁT VÉ";
        iconSvg = `<svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#A1A1AA" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>`;
        playFeedbackTone(false);
        break;
      case "NETWORK_ERROR":
        isTicketAccepted = false;
        boxModifier = "bgp-gate-result-box--danger";
        badgeModifier = "bgp-ops-badge--danger";
        badgeText = "LỖI KẾT NỐI";
        iconSvg = `<svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#F87171" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="1" y1="1" x2="23" y2="23"></line><path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55"></path><path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39"></path><path d="M8.53 16.11a6 6 0 0 1 6.95 0"></path><line x1="12" y1="20" x2="12.01" y2="20"></line></svg>`;
        playFeedbackTone(false);
        break;
      case "INVALID_QR_IMAGE":
        isTicketAccepted = false;
        boxModifier = "bgp-gate-result-box--danger";
        badgeModifier = "bgp-ops-badge--danger";
        badgeText = "ẢNH QR LỖI";
        iconSvg = `<svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#F87171" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"></path><line x1="9.5" y1="12.5" x2="14.5" y2="17.5"></line><line x1="14.5" y1="12.5" x2="9.5" y2="17.5"></line></svg>`;
        playFeedbackTone(false);
        break;
      case "NOT_FOUND":
      case "REJECTED":
      default:
        isTicketAccepted = false;
        boxModifier = "bgp-gate-result-box--danger";
        badgeModifier = "bgp-ops-badge--danger";
        badgeText = outcome === "NOT_FOUND" ? "KHÔNG TỒN TẠI" : "TỪ CHỐI";
        iconSvg = `<svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#F87171" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path><line x1="9" y1="9" x2="15" y2="15"></line><line x1="15" y1="9" x2="9" y2="15"></line></svg>`;
        playFeedbackTone(false);
        break;
    }

    statusBadge.className = `badge bgp-ops-badge ${badgeModifier}`;
    statusBadge.textContent = badgeText;
    emitScanTelemetry("ui_state_transition", { outcome: outcome, badgeText: badgeText });

    resultCard.innerHTML = "";
    const alertContainer = document.createElement("div");
    alertContainer.className = `bgp-gate-result-box ${boxModifier}`;
    alertContainer.setAttribute("role", "status");
    alertContainer.innerHTML = iconSvg;

    const heading = document.createElement("h3");
    heading.className = "bgp-gate-result__heading";
    heading.textContent = badgeText;
    alertContainer.appendChild(heading);

    const messageP = document.createElement("p");
    messageP.className = "bgp-gate-result__msg";
    messageP.textContent = (data && data.message) || "Đã xử lý yêu cầu.";
    alertContainer.appendChild(messageP);

    const actionsDiv = document.createElement("div");
    actionsDiv.className = "bgp-gate-result__actions";

    if (outcome === "ACCEPTED") {
      const btnNext = document.createElement("button");
      btnNext.id = "btnScanNext";
      btnNext.type = "button";
      btnNext.className = "bgp-btn bgp-btn--primary bgp-btn--sm";
      btnNext.textContent = "Quét vé tiếp theo";
      btnNext.addEventListener("click", () => resetScanState(true));
      actionsDiv.appendChild(btnNext);

      const btnRescan = document.createElement("button");
      btnRescan.id = "btnRescanCurrent";
      btnRescan.type = "button";
      btnRescan.className = "bgp-btn bgp-btn--outline bgp-btn--sm";
      btnRescan.textContent = "Quét lại vé này (Kiểm tra lại)";
      btnRescan.addEventListener("click", () => submitScan(lastScannedToken, true));
      actionsDiv.appendChild(btnRescan);
    } else if (outcome === "NETWORK_ERROR") {
      const btnRetry = document.createElement("button");
      btnRetry.type = "button";
      btnRetry.className = "bgp-btn bgp-btn--danger bgp-btn--sm";
      btnRetry.textContent = "Thử lại kết nối";
      btnRetry.addEventListener("click", () => submitScan(pendingRetryToken, true));
      actionsDiv.appendChild(btnRetry);
    } else {
      const btnClear = document.createElement("button");
      btnClear.id = "btnScanNext";
      btnClear.type = "button";
      btnClear.className = "bgp-btn bgp-btn--outline bgp-btn--sm";
      btnClear.textContent = "Quét vé khác";
      btnClear.addEventListener("click", () => resetScanState(true));
      actionsDiv.appendChild(btnClear);
    }

    alertContainer.appendChild(actionsDiv);
    resultCard.appendChild(alertContainer);

    if (ticketDetailsCard) {
      if (data && data.ticketId) {
        ticketDetailsCard.classList.remove("is-hidden");
        const codeEl = document.getElementById("resTicketCode");
        const movieEl = document.getElementById("resMovieTitle");
        const roomEl = document.getElementById("resRoomName");
        const seatEl = document.getElementById("resSeatLabel");
        const showtimeEl = document.getElementById("resShowtime");
        const scannedEl = document.getElementById("resScannedTime");

        const formatUtcToVn = (isoStr) => {
          if (!isoStr) return "-";
          const str = String(isoStr).trim();
          const normalized = /[zZ]|[+-]\d{2}:?\d{2}$/.test(str) ? str : `${str}Z`;
          const dt = new Date(normalized);
          if (Number.isNaN(dt.getTime())) return str;
          return `${dt.toLocaleString("vi-VN", {
            timeZone: "Asia/Ho_Chi_Minh",
            hour12: false
          })} (UTC+7)`;
        };

        if (codeEl) codeEl.textContent = data.ticketCode || `#${data.ticketId}`;
        if (movieEl) movieEl.textContent = data.movieTitle || "-";
        if (roomEl) roomEl.textContent = data.roomName || "-";
        if (seatEl) seatEl.textContent = data.seatLabel || "-";
        if (showtimeEl) {
          showtimeEl.textContent = data.showtimeStartUtc
            ? formatUtcToVn(data.showtimeStartUtc)
            : "-";
        }
        if (scannedEl) {
          scannedEl.textContent = data.scannedUtc
            ? formatUtcToVn(data.scannedUtc)
            : formatUtcToVn(new Date().toISOString());
        }
      } else {
        ticketDetailsCard.classList.add("is-hidden");
      }
    }
  }

  // Camera lifecycle via Html5Qrcode (inline banner on error — zero window.alert)
  async function startCamera() {
    if (isScanning) return;
    hideCameraError();
    try {
      if (placeholder) placeholder.classList.add("is-hidden");
      html5QrCode = new Html5Qrcode("reader");

      const config = {
        fps: 10,
        qrbox: { width: 250, height: 250 },
        aspectRatio: 1.0
      };

      await html5QrCode.start(
        { facingMode: "environment" },
        config,
        (decodedText) => {
          submitScan(decodedText);
        },
        () => {
          // Ignore per-frame decoding misses
        }
      );

      isScanning = true;
      if (btnStart) btnStart.disabled = true;
      if (btnStop) btnStop.disabled = false;
      emitScanTelemetry("camera_started", { elementId: "reader" });
    } catch (err) {
      if (placeholder) placeholder.classList.remove("is-hidden");
      emitScanTelemetry("camera_start_failed", { reason: "camera_unavailable" });
      showCameraError(err && err.message ? err.message : String(err || ""));
    }
  }

  async function stopCamera() {
    if (!isScanning || !html5QrCode) return;
    try {
      await html5QrCode.stop();
      html5QrCode.clear();
      html5QrCode = null;
    } catch {
      // Suppress harmless stop error
    } finally {
      isScanning = false;
      if (btnStart) btnStart.disabled = false;
      if (btnStop) btnStop.disabled = true;
      if (placeholder) placeholder.classList.remove("is-hidden");
      emitScanTelemetry("camera_stopped", { elementId: "reader" });
    }
  }

  if (btnStart) btnStart.addEventListener("click", startCamera);
  if (btnStop) btnStop.addEventListener("click", stopCamera);

  if (showtimeSelect) {
    showtimeSelect.addEventListener("change", () => {
      resetScanState(false);
    });
  }

  function loadImageFromFile(file) {
    return new Promise((resolve, reject) => {
      const objectUrl = URL.createObjectURL(file);
      const img = new Image();
      img.onload = () => {
        URL.revokeObjectURL(objectUrl);
        resolve(img);
      };
      img.onerror = () => {
        URL.revokeObjectURL(objectUrl);
        reject(new Error("Invalid or corrupted image file."));
      };
      img.src = objectUrl;
    });
  }

  function createPaddedCanvas(img, targetSize, padding, imageSmoothing) {
    const drawW = targetSize || img.naturalWidth || img.width;
    const drawH = targetSize || img.naturalHeight || img.height;
    const canvas = document.createElement("canvas");
    canvas.width = drawW + padding * 2;
    canvas.height = drawH + padding * 2;
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.fillStyle = "#FFFFFF";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.imageSmoothingEnabled = !!imageSmoothing;
    ctx.drawImage(img, padding, padding, drawW, drawH);
    return canvas;
  }

  // Deterministic multi-stage QR file decoding pipeline
  async function decodeQrFileDeterministically(file) {
    const scanner = getOrCreateFileScanner();
    emitScanTelemetry("file_decode_started", {
      fileSize: file.size,
      fileType: file.type || "unknown"
    });

    let img = null;
    try {
      img = await loadImageFromFile(file);
      emitScanTelemetry("image_loaded", {
        width: img.naturalWidth || img.width,
        height: img.naturalHeight || img.height
      });
    } catch (loadErr) {
      emitScanTelemetry("file_decode_failed", { stage: "image_load_error" });
      throw loadErr;
    }

    const natW = img.naturalWidth || img.width || 256;
    const canvasAttempts = [
      { label: "native_pad48", size: 0, pad: 48, smooth: false },
      { label: "native_2x_pad64", size: natW * 2, pad: 64, smooth: false },
      { label: "scale_185_pad32", size: 185, pad: 32, smooth: false },
      { label: "scale_222_pad36", size: 222, pad: 36, smooth: false },
      { label: "scale_259_pad40", size: 259, pad: 40, smooth: false },
      { label: "scale_296_pad44", size: 296, pad: 44, smooth: false },
      { label: "scale_370_pad48", size: 370, pad: 48, smooth: false },
      { label: "scale_444_pad56", size: 444, pad: 56, smooth: false },
      { label: "scale_246_pad36", size: 246, pad: 36, smooth: false },
      { label: "scale_328_pad40", size: 328, pad: 40, smooth: true }
    ];

    // Stage 0: Native Chromium BarcodeDetector on clean padded canvas when supported
    if (typeof window.BarcodeDetector === "function") {
      try {
        const nativeDetector = new window.BarcodeDetector({ formats: ["qr_code"] });
        for (const cfg of [canvasAttempts[0], canvasAttempts[1]]) {
          const nativeCanvas = createPaddedCanvas(img, cfg.size, cfg.pad, cfg.smooth);
          const detected = await nativeDetector.detect(nativeCanvas);
          if (Array.isArray(detected) && detected.length > 0) {
            const rawVal = detected[0] && detected[0].rawValue;
            if (rawVal && typeof rawVal === "string" && rawVal.trim().length > 0) {
              emitScanTelemetry("file_decode_succeeded", {
                stage: `barcode_detector_${cfg.label}`,
                length: rawVal.trim().length
              });
              return rawVal.trim();
            }
          }
        }
      } catch {
        // Proceed seamlessly to ZXing multi-scale pipeline
      }
    }

    if (scanner.qrcode && typeof scanner.qrcode.decodeRobustlyAsync === "function") {
      for (const cfg of canvasAttempts) {
        try {
          const canvas = createPaddedCanvas(img, cfg.size, cfg.pad, cfg.smooth);
          const qrResult = await scanner.qrcode.decodeRobustlyAsync(canvas);
          const text = qrResult && (qrResult.text || qrResult.decodedText);
          if (text && typeof text === "string" && text.trim().length > 0) {
            emitScanTelemetry("file_decode_succeeded", {
              stage: cfg.label,
              length: text.trim().length
            });
            return text.trim();
          }
        } catch {
          // Proceed to next deterministic canvas scale
        }
      }
    }

    // Stage 2: Fallback to Html5Qrcode.scanFile on dedicated #qrFileScratch container
    try {
      const scanned = await scanner.scanFile(file, false);
      try {
        scanner.clear();
      } catch {}
      if (scanned && typeof scanned === "string" && scanned.trim().length > 0) {
        emitScanTelemetry("file_decode_succeeded", {
          stage: "scanFile_direct",
          length: scanned.trim().length
        });
        return scanned.trim();
      }
    } catch {
      try {
        scanner.clear();
      } catch {}
    }

    emitScanTelemetry("file_decode_failed", { stage: "exhausted_all_stages" });
    throw new Error("Unable to decode QR code from uploaded image.");
  }

  if (qrFileInput) {
    qrFileInput.addEventListener("change", async (e) => {
      if (!e.target.files || e.target.files.length === 0) return;
      const file = e.target.files[0];

      if (qrUploadFileName) {
        qrUploadFileName.textContent = `Đang đọc: ${file.name}`;
      }

      emitScanTelemetry("file_selected", {
        fileSize: file.size,
        fileType: file.type || "unknown"
      });

      if (isScanning) {
        await stopCamera();
      }

      isTicketAccepted = false;
      statusBadge.className = "badge bgp-ops-badge bgp-ops-badge--info";
      statusBadge.textContent = "Đang đọc ảnh QR...";

      try {
        const decodedText = await decodeQrFileDeterministically(file);
        await submitScan(decodedText, true);
      } catch {
        renderResult({
          outcome: "INVALID_QR_IMAGE",
          isSuccess: false,
          message:
            "Không tìm thấy mã QR hợp lệ trong hình ảnh tải lên. Vui lòng chọn ảnh mã QR rõ nét hoặc nhập mã thủ công."
        });
      } finally {
        qrFileInput.value = "";
        if (qrUploadFileName) {
          qrUploadFileName.textContent = `Đã xử lý: ${file.name}`;
        }
      }
    });
  }

  if (manualForm && manualInput) {
    manualForm.addEventListener("submit", (e) => {
      e.preventDefault();
      const val = manualInput.value;
      if (!val || !val.trim()) return;
      isTicketAccepted = false;
      submitScan(val, true);
      manualInput.value = "";
    });
  }
})();
