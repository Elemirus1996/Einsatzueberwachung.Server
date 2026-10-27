#!/usr/bin/env python3
"""
Fetches GitHub releases and injects a changelog section into docs/index.html.
Usage: GITHUB_TOKEN=<token> python docs/generate_changelog.py docs/index.html
"""

import json
import os
import re
import sys
import urllib.request
from datetime import datetime, timezone

REPO = "Elemirus1996/Einsatzueberwachung.Server"
API_URL = f"https://api.github.com/repos/{REPO}/releases?per_page=20"
MAX_ENTRIES = 8

MARKER_START = "<!-- CHANGELOG_START -->"
MARKER_END = "<!-- CHANGELOG_END -->"


def fetch_releases(token: str) -> list:
    req = urllib.request.Request(API_URL)
    req.add_header("Authorization", f"Bearer {token}")
    req.add_header("Accept", "application/vnd.github.v3+json")
    req.add_header("User-Agent", "changelog-generator/1.0")
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode())


def markdown_to_html(text: str) -> str:
    """Minimal Markdown → HTML for release bodies."""
    if not text:
        return ""

    html_lines = []
    in_list = False
    in_code = False

    for line in text.splitlines():
        # Code blocks (```bash ... ```)
        if line.strip().startswith("```"):
            if in_list:
                html_lines.append("</ul>")
                in_list = False
            html_lines.append("</code></pre>" if in_code else "<pre><code>")
            in_code = not in_code
            continue
        if in_code:
            html_lines.append(_escape(line))
            continue
        # Headings
        if line.startswith("### "):
            if in_list:
                html_lines.append("</ul>")
                in_list = False
            html_lines.append(f"<h4>{_escape(line[4:].strip())}</h4>")
        elif line.startswith("## "):
            if in_list:
                html_lines.append("</ul>")
                in_list = False
            html_lines.append(f"<h4>{_escape(line[3:].strip())}</h4>")
        # Bullet points
        elif re.match(r"^[-*] ", line):
            if not in_list:
                html_lines.append("<ul>")
                in_list = True
            html_lines.append(f"<li>{_inline(line[2:].strip())}</li>")
        # Empty line
        elif line.strip() == "":
            if in_list:
                html_lines.append("</ul>")
                in_list = False
        else:
            if in_list:
                html_lines.append("</ul>")
                in_list = False
            html_lines.append(f"<p>{_inline(line.strip())}</p>")

    if in_list:
        html_lines.append("</ul>")
    if in_code:
        html_lines.append("</code></pre>")

    return "\n".join(html_lines)


def _escape(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def _inline(text: str) -> str:
    text = _escape(text)
    # Bold
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    # Code
    text = re.sub(r"`(.+?)`", r"<code>\1</code>", text)
    return text


def release_to_html(release: dict, open_entry: bool = False) -> str:
    tag = _escape(release.get("tag_name", ""))
    name = _escape(release.get("name") or tag)
    body = release.get("body") or ""

    published_at = release.get("published_at", "")
    try:
        dt = datetime.fromisoformat(published_at.replace("Z", "+00:00"))
        date_str = dt.strftime("%d.%m.%Y")
    except (ValueError, AttributeError):
        date_str = ""

    body_html = markdown_to_html(body)
    body_block = f'<div class="changelog-body">{body_html}</div>' if body_html else ""

    # Collapsible entry (layout in docs/index.html); the newest one is open
    return f"""\
<details class="changelog-entry"{" open" if open_entry else ""}>
  <summary>
    <span class="changelog-version">{tag}</span>
    <span class="changelog-date">{date_str}</span>
    <span class="changelog-name">{name if name != tag else ""}</span>
  </summary>
  {body_block}
</details>"""


def generate_section_html(releases: list) -> str:
    # Published regular releases only (no drafts, no betas)
    releases = [r for r in releases if not r.get("draft") and not r.get("prerelease")][:MAX_ENTRIES]
    if not releases:
        return '<div class="changelog-empty">Noch keine Releases verfügbar.</div>'
    return "\n".join(release_to_html(r, i == 0) for i, r in enumerate(releases))


def update_index(html_path: str, section_html: str, count: int) -> None:
    with open(html_path, encoding="utf-8") as f:
        content = f.read()

    pattern = re.compile(
        re.escape(MARKER_START) + r".*?" + re.escape(MARKER_END),
        flags=re.DOTALL,
    )

    if not pattern.search(content):
        print(f"ERROR: Marker '{MARKER_START}' not found in {html_path}", file=sys.stderr)
        sys.exit(1)

    replacement = f"{MARKER_START}\n{section_html}\n{MARKER_END}"
    new_content = pattern.sub(replacement, content)

    with open(html_path, "w", encoding="utf-8") as f:
        f.write(new_content)

    print(f"Updated {html_path} with {count} release(s).")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(f"Usage: python {sys.argv[0]} <path/to/index.html>", file=sys.stderr)
        sys.exit(1)

    html_path = sys.argv[1]
    token = os.environ.get("GITHUB_TOKEN", "")
    if not token:
        print("ERROR: GITHUB_TOKEN environment variable not set.", file=sys.stderr)
        sys.exit(1)

    releases = fetch_releases(token)
    section_html = generate_section_html(releases)
    update_index(html_path, section_html, len(releases))
