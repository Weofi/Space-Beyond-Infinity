#!/usr/bin/env bash
# Assembles the GitHub Pages site for the wiki (used by .github/workflows/pages.yml).
# Keeps the repo layout, because the reader loads images and source links relative to Docs/Wiki.
set -euo pipefail
out="${1:-_site}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
rm -rf "$out"
mkdir -p "$out/Docs" "$out/Assets/Design" "$out/Assets/_Project/Art" "$out/Tools/Docs"
cp -R "$root/Docs/Wiki" "$out/Docs/Wiki"
cp -R "$root/Docs/screenshots" "$out/Docs/screenshots"
for f in AGENTS.md README.md CLAUDE.md Assets/Design/DESIGN.md Assets/_Project/Art/CREDITS.md Tools/Docs/player_guide.json; do
  [ -f "$root/$f" ] && cp "$root/$f" "$out/$f"
done
touch "$out/.nojekyll"
cat > "$out/index.html" <<'EOF'
<!doctype html>
<meta charset="utf-8">
<title>Voidward Wiki</title>
<meta http-equiv="refresh" content="0; url=Docs/Wiki/">
<link rel="canonical" href="Docs/Wiki/">
<a href="Docs/Wiki/">Voidward Wiki</a>
EOF
echo "Pages site assembled in $out"
