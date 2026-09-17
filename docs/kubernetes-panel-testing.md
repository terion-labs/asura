# Kubernetes panel: implementation and testing

The core panel is implemented on `codex/kubernetes-panel`. The design rationale is [ADR 0058](adr/0058-owned-kubernetes-panel.md); the original research and broader acceptance plan remain in [the planning document](kubernetes-panel-plan.md). This is an operational first implementation, not completion of every feature proposed in that plan.

## Using the panel

1. Add a Kubernetes connection in the unified connection editor. Enter the kubeconfig path as seen by the selected workspace backend, review it, choose an explicit context and namespace, and approve the displayed credential helper when required.
2. Open the connection from the launcher, a new panel, a saved screen or a workspace template. Each panel keeps its own context, resource kind and namespace. The namespace dropdown includes All namespaces, discovered namespaces and the configured namespace. Configured and observed values remain selectable when namespace listing is forbidden.
3. Browse grouped resource families using the navigator. Pods and Nodes have dedicated sortable tables. Selecting a row opens the right-hand properties drawer with related-resource links, labels, conditions, containers and events. The YAML / JSON tab opens indented JSON in the shared syntax-highlighted editor with line numbers; pasted YAML uses YAML highlighting. User drafts remain exactly as typed. Dry-run review shows formatted, highlighted JSON for the current and proposed resource. Pod views expose container selection, previous/current logs, follow, a terminal, read-only files and port forwarding. Service forwarding resolves ready matching pods and requires an explicit pod/port choice.
4. Edit a manifest and run server dry-run before applying. Scale, rollout restart, delete and node scheduling also have separate review and confirmation. Drain displays skipped/blocked pods and per-pod outcomes. Never retry an unknown outcome without inspecting current cluster state.
5. Workspace-owned forwards remain available after closing the original inspector. Open their browser or database action to use the workspace's private endpoint. Stop the forward explicitly or close the workspace. Live forwards are not recreated on recovery: their browser panels reopen at `about:blank` and database panels reopen as unconfigured pickers. Forwarded database passwords are not restored.
6. Current Pod and Node metrics populate the table and properties drawer automatically. The Metrics API is preferred; missing measurements fall back to discovered Prometheus or VictoriaMetrics query services through the Kubernetes API proxy. Multiple services no longer require a choice: automatic probing prefers a working provider with complete measurements, while the drawer still allows manual selection. Probing is bounded to eight candidates and 30 seconds; failed/empty discovery and unavailable providers are retried on Refresh. Common Prometheus Operator and kube-prometheus-stack services, VictoriaMetrics `vmselect` with tenant `0`, and `vmsingle` are recognized. Node Disk shows root-filesystem (`/`) used percentage, with used/capacity bytes in its tooltip and drawer; missing exporter samples remain unavailable. Pod history uses the selected provider. Helm releases are a navigator entry above the resource groups. Helm must be installed in the backend execution environment. Helm changes review an exact release revision; upgrade additionally requires an OCI chart digest and version.
7. Agent settings have separate Kubernetes Data, Control and Exec grants, all Off by default. Read tools return bounded resource references. Preview and commit are separate operations. Pod input uses Kubernetes Exec, not host command authority. Agent writes target existing resources; node and Helm administration remain native UI operations. Pod File Viewer panels do not expose agent file authority.

## Automated coverage

Fixture suites cover parse-only trust review, credential refresh, unknown resources, pagination, expired watches, bounded streaming, UID/resourceVersion conflicts, server dry-run, unknown write outcomes, exec channels/resize/exit, route cancellation, forwarded endpoint revocation and replay prevention. App tests cover review invalidation, Service candidate selection, split layouts and typed target persistence. New policy defaults and imported definitions are tested alongside existing session/agent contracts.

Pod terminals launched through the Shell action use the existing conservative remote-prompt idle check, including default `sh-5.1#` and `bash-5.2$` prompts. The fallback is limited to the UI's exact `/bin/sh` and `/bin/bash` launches. Arbitrary exec commands, active command output, alternate-screen programs and mouse-tracking programs retain close confirmation. Explicit shell-integration command-running signals take precedence over prompt text. Native terminal tests exercise idle, running and returned-to-prompt states through the real Kubernetes terminal factory with simulated exec streams; these are not live pod-exec acceptance tests.

Native DesignQa routes: `workspace-kubernetes`, `workspace-kubernetes-detail`, `workspace-kubernetes-nodes`, `workspace-kubernetes-narrow`, `workspace-kubernetes-manifest`, `workspace-kubernetes-namespaces`, the inspector tabs `workspace-kubernetes-node-maintenance`, `workspace-kubernetes-actions`, `workspace-kubernetes-ports`, `workspace-kubernetes-shell` and `workspace-kubernetes-logs`, and `workspace-kubernetes-helm` with `workspace-kubernetes-helm-change`. Captures use the real MainWindow, panel controls and theme with explicitly labelled synthetic data. The Lens comparison set uses 1840×1196 for Pods, the Pod drawer and Nodes, and 1080×680 for two narrow panels. The inspected captures and three normalized side-by-side comparisons are in `artifacts/design-qa/kubernetes-lens/`; the local review report is `design-qa.md`.

A Native AOT spike exercises the selected SDK's unknown-resource JSON, watch and YAML path. It does not substitute for publishing and exercising every packaged desktop/guest target.

## Live read-only acceptance

Run after building `src/Asura.Backend/Asura.Backend.csproj` with the repository SDK:

```sh
python3 scripts/test-kubernetes-live.py orbstack browsercity-core browsercity-rancher \
  --trust-existing-exec --namespace kube-system --read-logs --read-observability
```

The opt-in flag permits the exact credential helper found by parse-only review; it does not persist trust. The script launches the real private worker and checks discovery, list, inspect, bounded logs and manifest conversion. Optional checks read Metrics API and Helm release/history metadata. Output includes only counts and error categories, not endpoints, credentials, manifests or log contents.

To check a specific in-cluster Prometheus provider through the same private worker and Kubernetes API service proxy:

```sh
python3 scripts/test-kubernetes-live.py browsercity-rancher \
  --trust-existing-exec --namespace kube-system \
  --prometheus monitoring/prometheus-operated:9090
```

This reads current Pod and Node usage with fixed bulk queries and one hour of CPU and memory history for the first listed pod. It rejects cross-namespace Pod samples and reports only availability and sample counts.

For the existing Core VictoriaMetrics cluster:

```sh
python3 scripts/test-kubernetes-live.py browsercity-core \
  --trust-existing-exec --namespace kube-system \
  --prometheus browsercity-observability/vmselect-monitoring:8481 --prometheus-tenant 0
```

The tenant is a validated numeric account or account:project, and both current and history queries use the [documented VictoriaMetrics tenant route](https://docs.victoriametrics.com/victoriametrics/url-examples/#api-v1-query). Exporters and ingestion/storage services are excluded from query-provider discovery. Node disk uses the [node-exporter filesystem metrics](https://prometheus.io/docs/guides/node-exporter/), restricted to the root mount, with pseudo filesystems excluded. Used bytes are total minus free bytes; mountpoints are not summed. The Metrics API cannot supply this disk measurement, so Prometheus-compatible providers supplement it even when CPU and memory are available there.

Direct read-only checks on 2026-09-17 reached:

| Context | Kubernetes version | Authentication | Discovery |
| --- | --- | --- | --- |
| orbstack | v1.35.6+orb1 | Client certificate | 70 served resources |
| browsercity-core | v1.36.1 | Existing OCI exec helper | 180 served resources |
| browsercity-rancher | v1.36.1 | Existing OCI exec helper | 228 served resources |

Each passed list/inspect, bounded logs, manifest conversion and Helm 4 release-list reads. The Metrics API returned unavailable on these clusters. A subsequent private-worker IPC check on browsercity-rancher successfully used the specified Prometheus service: 25 container CPU and memory samples in kube-system, three Node CPU and memory samples, and one selected-pod history series with 61 samples for each metric. This verifies the optional provider's serialized request, service-proxy queries and response projection. The harness does not exercise desktop chart rendering. No Helm releases were present in the selected kube-system namespaces, so live release history was not exercised. Initial Helm reads found a Helm 4 command-flag incompatibility; the corrected command passed the rerun on all three contexts. Discovery includes every served version, and counts can change with cluster availability.

The rebuilt macOS development app was also exercised against the existing browsercity-core connection: 130 Pods, three Nodes, typed namespace selection, sorting, text filtering, Pod properties, Pod-to-Node navigation and closing the drawer. The final drawer remained opaque under the user's translucent MacOsLiquidGlass theme; the navigator displayed one Events entry and the inspector displayed one metrics-unavailable explanation. Desktop chart rendering was checked with the labelled DesignQa fixture, not a live Rancher profile. No new connection or persistent credential-helper grant was saved for this check.

The manifest/selector follow-up was verified in the native app against the existing browsercity-rancher profile: All namespaces loaded 50 Pods; choosing argocd loaded seven; restarting retained the argocd selection and its visible label. The selected live Pod manifest displayed indented JSON, line numbers and syntax colors. No manifest was edited or submitted during this check.

The metrics follow-up found two separate discovery failures: Rancher's Prometheus aliases were considered ambiguous, and Core's VictoriaMetrics `vmselect` label was not recognized. After the fix, read-only acceptance through the rebuilt private worker passed on both contexts. In kube-system, Core returned 28 container CPU/memory entries and Rancher returned 25. Each returned three node CPU, memory and root-filesystem measurements, all matching the three listed Kubernetes node identities. Each also returned 61 samples for both CPU and memory history for the selected pod. Automatic selection, fallback between multiple providers, partial API supplementation and delayed-response handling were verified in App tests; these live commands select their provider explicitly to validate the serialized engine path.

## Acceptance boundaries

No live resource mutation, pod command, container-file read, port-forward consumer, drain or Helm change was performed. These require a selected disposable namespace/workload before live acceptance. The harness does not write kubeconfig or cluster resources; it invokes the existing approved authentication helper when needed.

The implementation currently requires WebSocket exec v5, POSIX shell/head for container files and an installed Helm executable for Helm operations. Container files are read-only. Recovery and autosave turn pod terminals and pod File Viewer panels into resource browsers; reopening either operation requires an explicit action. Agent structured writes cover existing-resource apply, scale, restart and delete; node and Helm administration use the native UI. Imported managed credentials remain detached until explicitly provisioned. The linked kubeconfig editor reviews one file; it does not offer a KUBECONFIG merge editor.

Packaged Linux guest, service-VM, SSH/proxy/VPN WebSocket interoperability, other desktop OSes and screen-reader acceptance remain release checks. Release legal evidence must be renewed against the changed dependency closure; prior signed approval hashes are not rewritten as if they authorized new dependencies.

Automatic VictoriaMetrics cluster discovery uses tenant `0`. Explicit UI configuration for other tenants and authenticated query gateways is tracked by `asura-dp53.15`; the backend and live harness accept a validated numeric account:project tenant.

## Repository gate

The automatic metrics follow-up passes all 2,190 App tests and 88 Kubernetes engine tests. Formatting, dependency audit and the warning-free Release solution build pass. Its full gate still stops at the existing owner-approved release-evidence mismatch (294 passed, one failed in the first test project).

The pod-shell idle fix passes all 175 Terminal tests, including the native Kubernetes terminal factory regressions. Its full-gate run passed formatting, dependency audit and the warning-free Release build, then stopped at the same owner-approved release-evidence mismatch described below.

For the Lens UI revision, the warning-free Release solution build, dependency audit and formatting passed. `./scripts/check.sh --full` ran the first test project: 294 passed and one failed at the owner-approved release-evidence check, which stops that script. The affected projects were then run independently: App 2,153 passed; architecture 857 passed with five existing environment skips; Kubernetes engine 70 passed. The manifest editor and namespace selector follow-up passed all 2,164 App tests, including 77 Kubernetes tests. The earlier implementation baseline ran all 28 projects (8,501 passed, one approval failure, 18 existing environment skips). New dependency hashes require a renewed owner decision; no test or hook was disabled. See [the exact dependency change](kubernetes-dependency-review.md).

## Remaining product scope

The implementation epic `asura-dp53` remains open. The following original-plan items require follow-up beyond the live and release checks above:

| Tracking | Current behavior | Remaining work |
| --- | --- | --- |
| `asura-dp53.2` | Linked-file review and explicit context; backend contracts for managed credentials and SSH hops | Kubeconfig discovery, paste/managed provisioning, merged files, new SSH-hop selection and connection-test UI |
| `asura-dp53.3` | Grouped navigator, sortable Pod/Node tables, generic resource table, text search, namespace selector, structured inspector, labels, related-resource links/events, highlighted manifest editor | Configurable/CRD-specific columns, multiple namespace selection and navigation history |
| `asura-dp53.3` | Expired-watch relist; manual reconnect after normal termination | Retry/backoff, visibility-aware suspension and coalesced UI updates |
| `asura-dp53.4` | Launcher, chooser, screens, workspace recovery, agent capabilities and shared panel chrome | Kubernetes-specific command/keymap preferences and operational notifications; persisted filter/selection/column preferences |
| `asura-dp53.5` | Bounded current/previous logs and follow; read-only container files; private forwards | Tail/since/search/export controls, workload aggregation, uploads/transfers and forwarded HTTPS original-host configuration |
| `asura-dp53.6` | Existing-resource edits, dry-run, scale/restart/delete and node maintenance | Create-from-YAML, multi-document operations, Job-from-CronJob, suspend/resume, highlighted diffs, dedicated finalizer/force-delete/RBAC workflows and access-review-aware action availability |
| `asura-dp53.6` | Secret values redacted and agent Secret manifests excluded | Separate controlled reveal/copy/edit authority and UI |
| `asura-dp53.7` | Helm release/history, reviewed upgrade/rollback/uninstall and optional metrics | Helm install and retained-resource presentation; full release/platform acceptance |

Owner dependency approval is tracked by `asura-dp53.8`; disposable live operations and routed/platform acceptance by `asura-dp53.9`.
