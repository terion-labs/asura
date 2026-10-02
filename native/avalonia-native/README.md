# Patched Avalonia Native for macOS

Avalonia.Native 12.0.5's native accessibility bridge leaks returned COM
references and has a cycle through its notification node, Objective-C element,
and managed peer. Removed visual lines retain their documents; removed chat
controls remain alive. The leaks occur in native windows with accessibility
active and are invisible to Avalonia.Headless tests.

`patches/0001-release-accessibility-ownership.patch` balances returned references
using upstream `ComPtr` and makes notification-node owners weak. An element can
be recreated against its existing node after macOS releases it. `SetNode` stores
a **borrowed** managed proxy, so the node keeps its initial native reference
until the managed `AvnAutomationPeer` finalizer calls `IAvnAutomationNode.Dispose`.
That method releases the initial reference. Do not replace this with a scoped
`comnew` at the call site: the managed proxy would point at freed memory.

The source revision is the one in the NuGet package's repository metadata:
`fee9c561ce036e8a3e8cee2397c75ca599b4790d`. The build script verifies the source
archive SHA-256 before applying the patches. These are native-only repairs; the
managed version and native COM ABI stay at 12.0.5. The script refuses a managed
version change until this patch and ABI are reviewed.

`avalonia-native.h` is the generated header for that revision's
`src/Avalonia.Native/avn.idl`, generated with MicroCom.CodeGenerator **0.11.4**
(the upstream pinned version). Upstream's `GenerateCppHeaders` target calls
`MicroComCodeGenerator.Parse(idl).GenerateCppHeader()`. Keeping the generated ABI
here avoids restoring an unrelated upstream build toolchain during packaging.
The header and patched source retain Avalonia's MIT license in `LICENSE`.

Run `./scripts/prepare-avalonia-native-macos.sh` to build the universal arm64/x64
library and run the native regressions. It requires full Xcode, uses the existing
macOS toolchain selector, and caches by source inputs and toolchain. Passing a
copied `libAvaloniaNative.dylib` path installs the verified library, license and
receipt. The existing class namespace fix still runs before signing; development,
release and native browser acceptance bundles all use this path. The full
repository gate runs the native regressions on macOS.

The native test checks 2,000 tree refreshes, exact reference counts, released
Objective-C elements, and recreating an element while its managed peer survives.
It fails against the unmodified NuGet library on its first refresh. Accessibility
is kept enabled.

A real Avalonia window using two `CodePreviewView` and two `CodeEditBox` controls
was also tested with 1,000 replacements of an 800-line synthetic resource:

| Build | Retained managed growth after collection |
| --- | ---: |
| Original controls and native library | 705.8 MiB |
| Undo retention repaired | 192.3 MiB |
| Undo and native accessibility repaired | 2.7 MiB |

A longer run of the prepared universal library retained **3.9 MiB after 10,000
refreshes**, with 234 MiB process RSS. The native scrollbar remained operable
through accessibility after the refresh test.

These are controlled reproduction results, not a memory ceiling for a user's
workspace. The user's production process was inspected only through aggregate
memory counters; its private heap was not dumped or uploaded.

## Window resizing

`patches/0002-consume-metal-resize-on-render-thread.patch` fixes a stale drawable
after an abrupt window resize. Upstream 12.0.5 applies pending Metal dimensions
only in a main-thread `BeginDrawing`. If the compositor draws the next frame
before that paint arrives, it continues using the previous dimensions and scale.
The patch snapshots size and scale together under a mutex and applies them on
either rendering thread. It preserves upstream's synchronous presentation for
main-thread frames and asynchronous presentation for compositor frames.

`tests/metal-resize.mm` calls the actual packaged bridge and checks both session
dimensions and real Metal texture dimensions. The unpatched library returns an
800×600 texture after an 800×600 → 1200×450 resize followed by a compositor frame.
The patched library also passes repeated coalesced resizes, a 1×1 surface,
backing-scale changes with unchanged pixel dimensions, and alternating drawing
threads. The test checks that main-thread frames restore asynchronous presentation.
Hosts without a Metal device (including hosted macOS VMs) explicitly report the
GPU test as skipped; packaging remains available there. Any drawing or bridge
failure on a Metal-capable host fails the gate. The before/after regression above
was verified on a physical Apple Silicon Mac.
