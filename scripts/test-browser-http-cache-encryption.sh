#!/usr/bin/env bash
set -euo pipefail

# Uses only a disposable profile and fixture cookies. Never opens user data.
repository_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime="${1:-${repository_dir}/native/artifacts/osx-arm64/cef}"
signing_identity="${ASURA_BROWSER_TEST_SIGNING_IDENTITY:--}"
[[ "$(uname -s):$(uname -m)" == Darwin:arm64 ]] || { echo "Requires Apple Silicon macOS." >&2; exit 1; }
[[ -f "${runtime}/cef-runtime-build-receipt.json" ]] || { echo "Build the CEF runtime first." >&2; exit 1; }
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/asura-http-cache.XXXXXX")"
keychain_service="AsuraTest$(openssl rand -hex 6)"
cleanup() {
    local result=$?
    # Only this run's synthetic item. Never read or delete Chromium/Asura keys.
    if ! security delete-generic-password -s "${keychain_service}" -a Chromium >/dev/null 2>&1; then
        echo "Could not remove disposable Keychain item ${keychain_service}." >&2
        result=1
    fi
    if [[ -n "${server_pid:-}" ]]; then
        kill "${server_pid}" 2>/dev/null || true
        wait "${server_pid}" 2>/dev/null || true
    fi
    rm -rf -- "${test_dir}"
    return "${result}"
}
trap cleanup EXIT
app="${test_dir}/SessionCookies.app"
mkdir -p "${app}/Contents/MacOS" "${app}/Contents/Frameworks" "${test_dir}/state"
cp "${repository_dir}/native/browser-profile-tests/Info.plist" "${app}/Contents/Info.plist"
ditto "${runtime}/Chromium Embedded Framework.framework" "${app}/Contents/Frameworks/Chromium Embedded Framework.framework"
python3 - "${repository_dir}/scripts/scope-cef-keychain.py" \
    "${app}/Contents/Frameworks/Chromium Embedded Framework.framework/Chromium Embedded Framework" \
    "${keychain_service}" <<'PY'
import importlib.util
from pathlib import Path
import sys
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("scope_cef", sys.argv[1])
scope = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scope)
scope.SERVICES["fixture"] = sys.argv[3].encode("ascii") + b"\0"
scope.scope_framework(Path(sys.argv[2]), "release", "fixture")
PY
codesign --force --sign - "${app}/Contents/Frameworks/Chromium Embedded Framework.framework"
for helper_app in "${runtime}/"*.app; do
    ditto "${helper_app}" "${app}/Contents/Frameworks/$(basename "${helper_app}")"
done
cp "${runtime}/libexclr8cef.dylib" "${app}/Contents/Frameworks/"
xcrun clang -std=c11 -Wall -Wextra -Werror \
    -I "${repository_dir}/vendor/exclr8cef/native/shim" \
    "${repository_dir}/native/browser-profile-tests/cache_encryption.c" \
    -L "${app}/Contents/Frameworks" -lexclr8cef -framework Security -framework CoreFoundation \
    -Wl,-rpath,@executable_path/../Frameworks \
    -o "${app}/Contents/MacOS/SessionCookies"
codesign --force --sign "${signing_identity}" "${app}"
helper="${app}/Contents/Frameworks/Asura Helper.app/Contents/MacOS/Asura Helper"
python3 - "${test_dir}" <<'PY_SERVER' &
from http.server import HTTPServer, BaseHTTPRequestHandler
from pathlib import Path
import sys
root = Path(sys.argv[1])
class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path != '/cache-fixture':
            self.send_error(404); return
        with (root / 'requests').open('a') as out: out.write('request\n')
        payload = b'<html><body>asura-cache-private-fixture-764291</body></html>'
        self.send_response(200)
        self.send_header('Content-Type', 'text/html')
        self.send_header('Cache-Control', 'public, max-age=86400')
        self.send_header('Content-Length', str(len(payload)))
        self.end_headers(); self.wfile.write(payload)
    def log_message(self, *args): pass
server = HTTPServer(('127.0.0.1', 0), Handler)
(root / 'port').write_text(str(server.server_port))
server.serve_forever()
PY_SERVER
server_pid=$!
for attempt in {1..100}; do [[ -f "${test_dir}/port" ]] && break; sleep 0.05; done
url="http://127.0.0.1:$(cat "${test_dir}/port")/cache-fixture"
"${app}/Contents/MacOS/SessionCookies" "${test_dir}/state" "${helper}" "${url}"
python3 - "${test_dir}" <<'PY_VERIFY'
from pathlib import Path
import sys
root = Path(sys.argv[1])
cache = root / 'state/profile/Cache'
entries = [p for p in cache.rglob('*') if p.is_file() and p.name != 'index']
assert entries, 'No disk cache was created; encryption was not exercised'
for entry in entries:
    data = entry.read_bytes()
    assert b'asura-cache-private-fixture-764291' not in data, 'HTTP response leaked in plaintext'
# Native encrypted files start with the 40-byte salt/nonce header.
assert any(p.read_bytes()[0:1] == bytes([40]) for p in entries), 'No encrypted cache file header'
assert (root / 'requests').read_text().count('request') == 1
# Reproduce Chrome's last-used profile metadata written by opening DevTools.
# Only its HTTP cache should exist until the application explicitly restores it.
import json
state_path = root / 'state/Local State'
state = json.loads(state_path.read_text())
state.setdefault('profile', {})['last_used'] = 'deferred-profile'
state_path.write_text(json.dumps(state))
(root / 'state/deferred-profile/Cache').mkdir(parents=True)
PY_VERIFY
"${app}/Contents/MacOS/SessionCookies" "${test_dir}/state" "${helper}" "${url}"
[[ "$(wc -l < "${test_dir}/requests" | tr -d ' ')" == 1 ]] || { echo "Cache was not reused across processes." >&2; exit 1; }
[[ ! -e "${test_dir}/state/deferred-profile/Preferences" ]] || { echo "Chromium opened a deferred profile before restoration." >&2; exit 1; }
echo "Native encrypted HTTP cache survived restart without a network fetch."

# A different identity is denied this run's Keychain key. The disk backend must
# stay untouched even though the requested HTTP response is cacheable.
python3 - "${test_dir}" <<'PY_BEFORE'
from pathlib import Path
import hashlib, json, sys
root = Path(sys.argv[1]); cache = root / 'state/profile/Cache'
(root / 'cache-hashes.json').write_text(json.dumps({str(p.relative_to(cache)): hashlib.sha256(p.read_bytes()).hexdigest() for p in cache.rglob('*') if p.is_file()}, sort_keys=True))
PY_BEFORE
codesign --force --sign - --identifier sh.asura.tests.untrusted-cache-reader "${app}"
"${app}/Contents/MacOS/SessionCookies" "${test_dir}/state" "${helper}" "${url}"
python3 - "${test_dir}" <<'PY_DENIED'
from pathlib import Path
import hashlib, json, sys
root = Path(sys.argv[1]); cache = root / 'state/profile/Cache'
after = {str(p.relative_to(cache)): hashlib.sha256(p.read_bytes()).hexdigest() for p in cache.rglob('*') if p.is_file()}
assert json.loads((root / 'cache-hashes.json').read_text()) == after, 'Denied key access modified the disk cache'
assert (root / 'requests').read_text().count('request') == 2, 'Denied key access did not bypass the unreadable disk cache'
PY_DENIED
echo "Denied Keychain access leaves the disk cache untouched; no plaintext fallback."
