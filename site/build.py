"""Build the static Pages artifact using the canonical URL supplied by configure-pages."""
import argparse
import html
import shutil
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parent
DEFAULT_URL = "https://managedcode.github.io/LlmTestServer"
parser = argparse.ArgumentParser()
parser.add_argument("--base-url", default=DEFAULT_URL)
args = parser.parse_args()
base_url = args.base_url.rstrip("/")
parsed = urlparse(base_url)
if parsed.scheme != "https" or not parsed.netloc or parsed.query or parsed.fragment or any(c.isspace() for c in base_url):
    parser.error("The canonical base URL must be an absolute HTTPS URL without query or fragment.")
output = ROOT / "dist"
if output.exists():
    shutil.rmtree(output)
output.mkdir()
for name in ("index.html", "tokens.css", "styles.css", "site.js"):
    content = (ROOT / name).read_text()
    (output / name).write_text(content.replace(DEFAULT_URL, html.escape(base_url, quote=True)))
shutil.copytree(ROOT / "assets", output / "assets")
# Reuse the reviewed product captures without duplicating tracked raster assets.
for name in ("showcase-token-flow.jpg", "showcase-overview.jpg", "showcase-insights.jpg", "admin-dashboard-runtime.jpg"):
    shutil.copyfile(ROOT.parent / "docs" / "images" / name, output / "assets" / name)
(output / ".nojekyll").touch()
(output / "robots.txt").write_text(f"User-agent: *\nAllow: /\nSitemap: {base_url}/sitemap.xml\n")
(output / "sitemap.xml").write_text(
    '<?xml version="1.0" encoding="UTF-8"?>\n'
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">'
    f"<url><loc>{html.escape(base_url)}/</loc></url></urlset>\n"
)
print(f"Built {output} for {base_url}/")
