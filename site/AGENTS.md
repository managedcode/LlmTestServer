# Site

The site is static HTML/CSS.

Keep the first viewport focused on the product and include a real raster visual asset. Verify layout at desktop and mobile widths before shipping visual changes.

## Site commands

- Install: `npm ci --prefix site`
- Lint: `npm run lint --prefix site` (HTMLHint, Stylelint, JavaScript syntax)
- Build: `npm run build --prefix site`
- Production canonical: `python3 site/build.py --base-url "https://actual-pages-base-url"`; the Pages workflow supplies `configure-pages` output.
- Build output is `site/dist/`; only this directory is published. Keep tooling and local marketing originals out of the Pages artifact.
- `tokens.css` mirrors the dashboard's final warm canvas/ink/lime brand layer. Check the complete CSS cascade when synchronizing it; earlier theme blocks are not the final skin.
