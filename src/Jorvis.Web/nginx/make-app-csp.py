"""Writes the Content-Security-Policy for a Blazor app (the main app or the Sign In app), including the hash of the inline import map.

Blazor puts a build-specific import map in index.html, so its hash changes with every publish.
"""
import base64
import hashlib
import re
import sys

index_path, output_path = sys.argv[1], sys.argv[2]

html = open(index_path, encoding="utf-8").read()
match = re.search(r'<script type="importmap">(.*?)</script>', html, re.DOTALL)
if not match:
    sys.exit("No inline import map found in index.html")

digest = base64.b64encode(hashlib.sha256(match.group(1).encode("utf-8")).digest()).decode()

policy = "; ".join([
    "default-src 'self'",
    f"script-src 'self' 'wasm-unsafe-eval' 'sha256-{digest}'",
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
    "font-src 'self' https://fonts.gstatic.com data:",
    "img-src 'self' data:",
    "connect-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
])

with open(output_path, "w", encoding="utf-8") as f:
    f.write(f'add_header Content-Security-Policy "{policy}" always;\n')
