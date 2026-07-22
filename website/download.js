(() => {
  const REPO = "bobchomp/musicplaying";
  const API_URL = `https://api.github.com/repos/${REPO}/releases/latest`;

  const versionEl = document.getElementById("download-version");
  const buttonEl = document.getElementById("download-button");
  const metaEl = document.getElementById("download-meta");
  const errorEl = document.getElementById("download-error");

  function formatBytes(bytes) {
    if (!bytes && bytes !== 0) {
      return "";
    }
    const mb = bytes / (1024 * 1024);
    return `${mb.toFixed(1)} MB`;
  }

  function formatDate(iso) {
    try {
      return new Date(iso).toLocaleDateString(undefined, {
        year: "numeric",
        month: "long",
        day: "numeric",
      });
    } catch {
      return "";
    }
  }

  function showError() {
    versionEl.textContent = "Couldn't check the latest version automatically.";
    errorEl.style.display = "block";
  }

  fetch(API_URL, { headers: { Accept: "application/vnd.github+json" } })
    .then((response) => {
      if (!response.ok) {
        throw new Error(`GitHub API responded ${response.status}`);
      }
      return response.json();
    })
    .then((release) => {
      const asset = (release.assets || []).find((a) => a.name.toLowerCase().endsWith(".exe"));
      if (!asset) {
        throw new Error("No .exe asset found on the latest release");
      }

      const version = release.tag_name || release.name || "";
      versionEl.innerHTML = `Latest version: <strong>${version.replace(/^v/i, "")}</strong>`;
      buttonEl.href = asset.browser_download_url;
      buttonEl.removeAttribute("disabled");
      buttonEl.textContent = "Download for Windows";

      const metaParts = [];
      if (asset.size) {
        metaParts.push(formatBytes(asset.size));
      }
      if (release.published_at) {
        metaParts.push(`Released ${formatDate(release.published_at)}`);
      }
      metaEl.textContent = metaParts.join(" · ");
    })
    .catch(() => {
      showError();
    });
})();
