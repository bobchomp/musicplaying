# Music Display website

Static marketing site for Music Display — plain HTML/CSS/JS, no build step.

- `index.html` — landing page
- `download.html` + `download.js` — download page; fetches the latest GitHub release
  (`bobchomp/musicplaying`) via the GitHub API client-side and links directly to the `.exe`
  asset, falling back to the Releases page if that fails
- `styles.css` — shared styles
- `assets/icon.png` — app icon, reused as the site logo/favicon

## Deploying to Vercel

1. In the Vercel dashboard, import this GitHub repo as a new project.
2. Set the project's **Root Directory** to `website` (Settings → General → Root Directory), since
   this site lives in a subfolder rather than the repo root.
3. Framework preset: **Other** (no build command/output directory needed — it's static files).
4. Deploy. `vercel.json` sets clean URLs, so `/download` works without the `.html` extension.

No environment variables or secrets are needed — the download page calls the public,
unauthenticated GitHub REST API directly from the browser.
