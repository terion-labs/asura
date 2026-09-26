# Native agent browser acceptance

Run `./scripts/check-browser-agent-native.sh` on macOS arm64 after bootstrapping
the repository's native artifacts. It uses the pinned .NET SDK, assembles a
disposable development app, and runs the real CEF renderer twice: directly and
through the fixture's authenticated HTTP proxy. It opens a temporary window
and uses fresh profiles under `src/Asura.Desktop/obj/browser-agent-native.*`.
No installed application, personal profile, or external website is used.

Checks cover a cold hidden browser surface, navigation, accessibility snapshot
references, fill/check/click, retained cookies, and cancellation during native
typing. The routed run also proves that requests use the authenticated proxy
and a proxy-denied destination never reaches the origin through a direct
fallback. Restricted authority continues to fail before native dispatch.

The runner prints its evidence directory containing direct/proxy logs and CEF
profiles. It exits nonzero on failure and stops its fixture server on exit.
This is a platform-specific acceptance check in addition to `check.sh --full`;
it is not evidence for other platforms or arbitrary script evaluation. The
SessionHost tests separately cover selection of workspace-network authority
only after confirmed Full access authorization.
