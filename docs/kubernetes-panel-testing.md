# Kubernetes panel: implementation and testing

The core panel is implemented on `codex/kubernetes-panel`. The design rationale is [ADR 0058](adr/0058-owned-kubernetes-panel.md); the original research and broader acceptance plan remain in [the planning document](kubernetes-panel-plan.md). This is an operational first implementation, not completion of every feature proposed in that plan.

## Using the panel

1. Add a Kubernetes connection in the unified connection editor. Enter the kubeconfig path as seen by the selected workspace backend, review it, choose an explicit context and namespace, and approve the displayed credential helper when required.
2. Open the connection from the launcher, a new panel, a saved screen or a workspace template. Each panel keeps its own context, resource kind and namespace. A namespace can be typed even when namespace listing is forbidden.
3. Select a resource to inspect its manifest. Pod views expose container selection, previous/current logs, follow, a terminal, read-only files and port forwarding. Service forwarding resolves ready matching pods and requires an explicit pod/port choice.
4. Edit a manifest and run server dry-run before applying. Scale, rollout restart, delete and node scheduling also have separate review and confirmation. Drain displays skipped/blocked pods and per-pod outcomes. Never retry an unknown outcome without inspecting current cluster state.
5. Workspace-owned forwards remain available after closing the original inspector. Open their browser or database action to use the workspace's private endpoint. Stop the forward explicitly or close the workspace. Live forwards are not recreated on recovery: their browser panels reopen at `about:blank` and database panels reopen as unconfigured pickers. Forwarded database passwords are not restored.
6. Metrics and Helm are optional tabs. Historical charts require a Prometheus service reachable through the Kubernetes API proxy. Helm must be installed in the backend execution environment. Helm changes review an exact release revision; upgrade additionally requires an OCI chart digest and version.
7. Agent settings have separate Kubernetes Data, Control and Exec grants, all Off by default. Read tools return bounded resource references. Preview and commit are separate operations. Pod input uses Kubernetes Exec, not host command authority. Agent writes target existing resources; node and Helm administration remain native UI operations. Pod File Viewer panels do not expose agent file authority.

## Automated coverage

Fixture suites cover parse-only trust review, credential refresh, unknown resources, pagination, expired watches, bounded streaming, UID/resourceVersion conflicts, server dry-run, unknown write outcomes, exec channels/resize/exit, route cancellation, forwarded endpoint revocation and replay prevention. App tests cover review invalidation, Service candidate selection, split layouts and typed target persistence. New policy defaults and imported definitions are tested alongside existing session/agent contracts.

Native DesignQa routes: `workspace-kubernetes` and `workspace-kubernetes-narrow`. Captures use the real MainWindow, panel controls and theme. Final screenshots were regenerated and inspected at 1440×900 and 1080×680 with two narrow panels, in `artifacts/design-qa/kubernetes-final/`.

A Native AOT spike exercises the selected SDK's unknown-resource JSON, watch and YAML path. It does not substitute for publishing and exercising every packaged desktop/guest target.

## Live read-only acceptance

Run after building `src/Asura.Backend/Asura.Backend.csproj` with the repository SDK:

```sh
python3 scripts/test-kubernetes-live.py orbstack browsercity-core browsercity-rancher \
  --trust-existing-exec --namespace kube-system --read-logs --read-observability
```

The opt-in flag permits the exact credential helper found by parse-only review; it does not persist trust. The script launches the real private worker and checks discovery, list, inspect, bounded logs and manifest conversion. Optional checks read Metrics API and Helm release/history metadata. Output includes only counts and error categories, not endpoints, credentials, manifests or log contents.

Direct read-only checks on 2026-09-17 reached:

| Context | Kubernetes version | Authentication | Discovery |
| --- | --- | --- | --- |
| orbstack | v1.35.6+orb1 | Client certificate | 70 served resources |
| browsercity-core | v1.36.1 | Existing OCI exec helper | 180 served resources |
| browsercity-rancher | v1.36.1 | Existing OCI exec helper | 228 served resources |

Each passed list/inspect, bounded logs, manifest conversion and Helm 4 release-list reads. Metrics requests returned unavailable on these clusters. The command-line harness did not exercise live charts or the desktop panel UI. No Helm releases were present in the selected kube-system namespaces, so live release history was not exercised. Initial Helm reads found a Helm 4 command-flag incompatibility; the corrected command passed the rerun on all three contexts. Discovery includes every served version, and counts can change with cluster availability.

## Acceptance boundaries

No live resource mutation, pod command, container-file read, port-forward consumer, drain or Helm change was performed. These require a selected disposable namespace/workload before live acceptance. The harness does not write kubeconfig or cluster resources; it invokes the existing approved authentication helper when needed.

The implementation currently requires WebSocket exec v5, POSIX shell/head for container files and an installed Helm executable for Helm operations. Container files are read-only. Recovery and autosave turn pod terminals and pod File Viewer panels into resource browsers; reopening either operation requires an explicit action. Agent structured writes cover existing-resource apply, scale, restart and delete; node and Helm administration use the native UI. Imported managed credentials remain detached until explicitly provisioned. The linked kubeconfig editor reviews one file; it does not offer a KUBECONFIG merge editor.

Packaged Linux guest, service-VM, SSH/proxy/VPN WebSocket interoperability, other desktop OSes and screen-reader acceptance remain release checks. Release legal evidence must be renewed against the changed dependency closure; prior signed approval hashes are not rewritten as if they authorized new dependencies.

## Repository gate

The warning-free Release solution build, dependency audit, formatting and architecture checks passed. All 28 test projects were run, including projects after the failing full-gate project: 8,501 passed, one failed and 18 were skipped by their existing environment requirements (8,520 total). App tests passed 2,117/2,117; architecture tests passed 857 with five environment skips. The sole failure is the repository's owner-approved release-evidence check because new dependency hashes require a renewed decision. No test or hook was disabled. See [the exact dependency change](kubernetes-dependency-review.md).

## Remaining product scope

The implementation epic `asura-dp53` remains open. The following original-plan items require follow-up beyond the live and release checks above:

| Tracking | Current behavior | Remaining work |
| --- | --- | --- |
| `asura-dp53.2` | Linked-file review and explicit context; backend contracts for managed credentials and SSH hops | Kubeconfig discovery, paste/managed provisioning, merged files, new SSH-hop selection and connection-test UI |
| `asura-dp53.3` | Discovered kinds, fixed resource rows, text filter, single namespace or all, manifest inspector | Resource-family navigation, sortable/configurable/CRD columns, labels, multiple namespaces, related-resource/events inspector and navigation history |
| `asura-dp53.3` | Expired-watch relist; manual reconnect after normal termination | Retry/backoff, visibility-aware suspension and coalesced UI updates |
| `asura-dp53.4` | Launcher, chooser, screens, workspace recovery, agent capabilities and shared panel chrome | Kubernetes-specific command/keymap preferences and operational notifications; persisted filter/selection/column preferences |
| `asura-dp53.5` | Bounded current/previous logs and follow; read-only container files; private forwards | Tail/since/search/export controls, workload aggregation, uploads/transfers and forwarded HTTPS original-host configuration |
| `asura-dp53.6` | Existing-resource edits, dry-run, scale/restart/delete and node maintenance | Create-from-YAML, multi-document operations, Job-from-CronJob, suspend/resume, highlighted diffs, dedicated finalizer/force-delete/RBAC workflows and access-review-aware action availability |
| `asura-dp53.6` | Secret values redacted and agent Secret manifests excluded | Separate controlled reveal/copy/edit authority and UI |
| `asura-dp53.7` | Helm release/history, reviewed upgrade/rollback/uninstall and optional metrics | Helm install and retained-resource presentation; full release/platform acceptance |

Owner dependency approval is tracked by `asura-dp53.8`; disposable live operations and routed/platform acceptance by `asura-dp53.9`.
