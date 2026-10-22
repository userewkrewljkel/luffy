"use strict";
(function () {
  const c = window.LUFFY_CONFIG || {};
  const version = typeof c.version === "string" && /^\d+(\.\d+)*$/.test(c.version) ? c.version : "3.8.1";
  const label = `v${version}${c.beta ? " beta" : ""}`;
  const versionPill = document.getElementById("version-pill");
  const panelVersion = document.getElementById("panel-version");
  if (versionPill) versionPill.textContent = label;
  if (panelVersion) panelVersion.textContent = `LUFFY ${label} · Windows x64`;

  // Only accept HTTPS destinations, so unfinished or insecure links aren't published as working downloads.
  function isHttpsURL(raw) {
    try { const u = new URL(raw); return u.protocol === "https:" && !!u.hostname; }
    catch (_) { return false; }
  }
  function connect(id, url, external) {
    const a = document.getElementById(id);
    if (!a) return false;
    if (!isHttpsURL(url)) {
      a.removeAttribute("href");
      a.classList.add("is-unavailable");
      a.setAttribute("aria-disabled", "true");
      a.addEventListener("click", (e) => e.preventDefault());
      return false;
    }
    a.href = url;
    a.classList.remove("is-unavailable");
    a.removeAttribute("aria-disabled");
    if (external) { a.target = "_blank"; a.rel = "noopener noreferrer"; }
    return true;
  }
  const downloadReady = connect("download-button", c.downloadUrl, false);
  connect("download-button-secondary", c.downloadUrl, false);
  const donateReady = connect("donation-button", c.donationUrl, true);
  if (downloadReady) document.getElementById("download-message").textContent = `LUFFY ${label} · Direct download`;
  if (donateReady) document.getElementById("donation-message").textContent = "Optional. LUFFY stays free.";
}());
