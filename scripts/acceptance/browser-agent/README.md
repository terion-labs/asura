# Native agent browser acceptance

Run `./scripts/check-browser-agent-native.sh` on macOS arm64 after bootstrapping
the repository's native artifacts. It uses the pinned .NET SDK, assembles a
disposable development app, and runs the real CEF renderer twice: directly and
through the fixture's authenticated HTTP proxy. It opens a temporary window
and uses fresh profiles under `src/Asura.Desktop/obj/browser-agent-native.*`.
No installed application, personal profile, or external website is used.

The probe disables reflection-based JSON serialization, matching the shipping
Native AOT app. Its startup check refuses to run if this setting is lost. This
configuration reproduces the v0.1.65 screenshot failure in the former CDP helper.
Use `./scripts/check-browser-agent-native.sh --aot` to also compile the probe
with Native AOT and run both routes through that executable. It prints the
runtime mode so the evidence distinguishes managed checks from AOT checks.
Use the release toolchain's `DEVELOPER_DIR` and `ASURA_NATIVE_AOT_LINKER` when
running AOT locally. Release CI and rehearsal set these and require this probe
before packaging. `ASURA_BUILD_ARTIFACTS_ROOT` keeps generated files outside
the sealed, read-only source tree.

Checks cover a cold hidden browser surface, navigation, accessibility snapshot
references, custom onclick controls, viewport PNGs with coordinate bindings,
fill/check/click, retained cookies, rejection of native menu fallback for browser
keystrokes, and cancellation during native
typing. The routed run also proves that requests use the authenticated proxy
and a proxy-denied destination never reaches the origin through a direct
fallback. Restricted authority continues to fail before native dispatch.

The runner prints its evidence directory containing direct/proxy logs and CEF
profiles. It exits nonzero on failure and stops its fixture server on exit.
This is a platform-specific acceptance check in addition to `check.sh --full`;
it is not evidence for other platforms or arbitrary script evaluation. The
SessionHost tests separately cover selection of workspace-network authority
only after confirmed Full access authorization.
