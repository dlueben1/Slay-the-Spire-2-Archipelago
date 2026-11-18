"""Download Slay the Spire 2 enchantment data and icons for the web builder.

Fetches enchantment metadata from Spire Codex, keeps the fields the builder uses, writes
web/src/generated/enchantments.json and downloads each enchantment's icon into the matching
web/public path. This is the enchantment counterpart of download_latest_relics.ps1.

    python scripts/download_latest_enchantments.py
"""

import json
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
API_URL = "https://spire-codex.com/api/enchantments?lang=eng"
IMAGE_ORIGIN = "https://spire-codex.com"
OUTPUT_PATH = REPO_ROOT / "web" / "src" / "generated" / "enchantments.json"
WEB_PUBLIC_ROOT = REPO_ROOT / "web" / "public"
KEPT_FIELDS = ("id", "name", "description", "card_type", "applicable_to", "image_url")
# A generic client name: the request carries nothing about the person running it.
HEADERS = {"User-Agent": "sts2ap-web-data-download"}


def fetch(url: str) -> bytes:
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=30) as response:
        return response.read()


def main() -> None:
    records = json.loads(fetch(API_URL))
    enchantments = [{field: record.get(field) for field in KEPT_FIELDS} for record in records]
    enchantments.sort(key=lambda record: record["id"])
    OUTPUT_PATH.write_text(json.dumps(enchantments, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Wrote {len(enchantments)} enchantments to {OUTPUT_PATH.relative_to(REPO_ROOT)}")

    for record in enchantments:
        image_url = record["image_url"]
        if not image_url:
            continue
        target = WEB_PUBLIC_ROOT / image_url.lstrip("/")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(fetch(IMAGE_ORIGIN + image_url))
    print(f"Downloaded icons into {(WEB_PUBLIC_ROOT / 'static' / 'images' / 'enchantments').relative_to(REPO_ROOT)}")


if __name__ == "__main__":
    main()
