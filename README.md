# LUFFY website — ready-to-host static landing page

The page is in English so it can be shared beyond Denmark. It includes the existing LUFFY paw icon, responsive layout, a styled (illustrative) meter, features, FAQ, free download and optional donation link.

## IMPORTANT: Before sharing publicly

The download and donation links are **intentionally disabled**, because neither a hosted `.exe` nor a real payment URL was provided. The site does NOT falsely imply a working download.

1. Test your **compiled** LUFFY v3.8.1 `.exe` on Windows.
2. Upload that `.exe` to a GitHub Release (Assets), or another trustworthy file host.
3. Create your optional tipping/donation page (e.g. https://ko-fi.com/), then link the account to your PayPal or Stripe.
4. Edit **`config.js`**:

   ```js
   window.LUFFY_CONFIG = {
     version: "3.8.1",
     beta: true,
     downloadUrl: "https://...real-download-link...",
     donationUrl: "https://ko-fi.com/...your-profile..."
   };
   ```

5. Open `index.html` to preview locally. The other files must stay in the same folder.
6. Publish the full folder to GitHub Pages, Netlify Drop, Cloudflare Pages, or your own web host. On GitHub Pages: create a public repository, upload all site files at root, then go to **Settings → Pages** and select branch `main` and folder `/ (root)`.

## Free hosting recommendation

GitHub Pages hosts the website for free; GitHub Releases hosts downloadable app builds. Separate hosting of the binary from the website keeps updates simple. Your default URL will look like `https://YOURNAME.github.io/luffy/` until you add a custom domain.

See official docs:
- https://docs.github.com/en/pages/getting-started-with-github-pages/creating-a-github-pages-site
- https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases
- https://help.ko-fi.com/hc/en-us/articles/115003980093-How-do-I-get-paid

## Release safety checklist

- Confirm the EXE starts on a second Windows PC and can select an audio source.
- Publish a SHA-256 hash alongside the release (optional but recommended).
- The executable may be marked as an unknown publisher because it is unsigned. Consider code signing if wider distribution grows.
- Ensure that copyright, contact details, product claims and selected donation account are accurate.
- If handling donations as income, check the applicable tax/accounting rules.
- Replacing `downloadUrl` with a new real release link is all that's needed to update the button.

## Files

- `index.html` — main page
- `styles.css` — responsive design
- `config.js` — URL + version configuration
- `app.js` — connects buttons to real URLs, safely disables unavailable ones
- `assets/luffy-paw.svg` — LUFFY logo copied from the app's sources

No analytics, trackers, cookies, external fonts, third-party scripts, build tools, or server are used by this site.
