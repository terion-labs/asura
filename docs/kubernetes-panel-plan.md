# Kubernetes panel research and implementation plan

Status: proposed, implementation not started. Research date: 2026-09-17.
Repository baseline: `17c56b67e7526fceed0cc58467b19415542fd876`.
Research issue: `asura-alv9`. Implementation epic: `asura-dp53`.

## Executive summary

Add a native Kubernetes panel with a dedicated saved cluster profile, a Lens-inspired resource browser, and the same workspace, session, credential, networking and agent boundaries as other Asura panels. Use Docker as the presentation and hosted-session precedent, and Database as the saved-profile and execution-backend precedent. A cluster context needs its own identity; it should not become another shell `ConnectionKind`.

The preferred dependency is the official `KubernetesClient.Aot`, with 19.0.2 as the verified research baseline. Adoption is conditional on a short compatibility spike. The released AOT package omits watch helpers and generic-client conveniences, and its internal YAML helper does not establish arbitrary-manifest editing support. These are material gaps for a Lens-like browser, especially custom resources. KubeOps adds operator abstractions without resolving those gaps; KubeClient is an alternative with an unproven Native AOT path. [Official package](https://www.nuget.org/packages/KubernetesClient.Aot/19.0.2), [released AOT project](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/KubernetesClient.Aot/KubernetesClient.Aot.csproj).

Start with the complete read-only operational path: connect, discover resources, browse, inspect conditions and events, follow updates, and expose governed observations to the agent. Then add logs, pod terminals, managed port forwards, browser/database/file integration, and reviewed mutations. Helm and optional historical metrics complete the operational scope. Each stage includes recovery, permissions and error states rather than deferring them to polish.

The largest integration risk is connection execution. Existing backend HTTP streaming does not provide a complete Kubernetes transport. API requests, WebSockets and credential helpers must run in the selected owned backend and retire when route authority changes. A host-network fallback would violate the existing architecture.

This plan proposes seven dependency-linked implementation tasks. It does not claim a tested Kubernetes version matrix or production-ready SDK integration. The first task must settle those questions before substantial UI work.

## Introduction

The intended user is an Asura user operating development and production clusters alongside terminals, files, Git, browsers, databases and an agent. The goal is everyday cluster inspection and operations inside existing workspaces. Multiple clusters are separate panel instances that can be placed side by side. A fleet-wide inventory is not required for the first release.

Research covered current public Lens documentation and its official layout screenshot, historical OpenLens source, published NuGet metadata, the official client's released source, Kubernetes API documentation and the current Asura code. Repository inspection included the Docker panel, unified connection editor, durable definitions, session host, connection backend, agent capability policy, recovery, packaging and design QA. The report distinguishes observed behavior from proposed Asura behavior. All designs and limits below are proposals unless explicitly described as current code or documented upstream behavior.

The assumption behind “all surrounding features” is full participation in Asura's existing application features, plus normal Lens-style Kubernetes operations. Lens accounts, commercial collaboration, cloud fleet discovery and vulnerability scanning are separate products. They are not dependencies of this panel. No live cluster, installed Lens application or cloud credential flow was exercised during this research.

## Main analysis

### 1. What to take from Lens and OpenLens

Current Lens separates its resource navigator, view tabs, main content and an operational dock. Its pod table combines namespace and text filtering with configurable columns and contextual actions. That gives a useful task sequence: select scope, find a resource, inspect its state, then act without losing the resource context. [Lens layout](https://docs.lenshq.io/k8slens/using-lens/layout/), [Pods view](https://docs.lenshq.io/k8slens/using-lens/workloads/pods/).

Asura already has global workspace navigation and panel docking. Keep those, and place only Kubernetes-specific navigation inside the panel. The existing `DockerRuntimePanelView.axaml` and `design/qa/current/workspace-docker-logs.png` provide the local precedent: a resource-family rail, resource selection and a detail area with operational tabs. Reuse Asura's theme tokens, focus chrome, density settings, icons, empty states and terminal presenter. Do not embed Electron or copy Lens's whole outer window into a panel.

Recommended panel composition:

| Region | Proposed behavior |
| --- | --- |
| Header | Saved profile/context, connection health, namespace scope, reconnect and panel controls. Scope stays visible while editing or acting. |
| Resource navigator | Overview, Workloads, Network, Configuration, Storage, Access control, Events, Custom resources, Helm and Port forwards. Searchable and collapsible. |
| Resource table | Virtualized rows, stable selection, sorting, text/label filters, namespace column when needed, configurable widths/order/visibility. |
| Inspector | Summary and conditions first, related resources, events, YAML, metrics and resource-specific actions. Back/forward history remains panel-local. |
| Operational detail | Logs, shell or YAML editor. “Open beside” creates an ordinary Asura panel; no second global tab system. |
| Narrow panel | Replace the rail with a resource selector; inspector occupies the content area with a back action. Avoid a permanently compressed three-column layout. |

Choose actual responsive thresholds through the existing headless design QA suite. Test narrow split panes, maximized panes, floating windows, compact/comfortable density and enlarged text. Search and filters must remain keyboard reachable. A table refresh must not steal focus or jump scroll position. Status uses text and icons as well as color. Accessibility names identify context, namespace, resource and action; screen readers should not announce every watch event.

Connection onboarding should discover candidate local kubeconfigs without executing them, allow file/paste import, and show contexts before connection. Lens documents both local discovery and import. Asura should bind the chosen context explicitly and never mutate the user's `current-context`. Context names are labels, not trustworthy server identities. Duplicate names from different files need source labels. Relative credential paths retain their source directory semantics, and merged-file precedence needs tests. [Lens cluster import](https://docs.lenshq.io/k8slens/getting-started/add-clusters/add-local-cluster/), [Kubernetes kubeconfig](https://kubernetes.io/docs/concepts/configuration/organize-cluster-access-kubeconfig/).

Namespace-restricted accounts must be useful. Lens permits explicit accessible namespaces when listing namespaces is forbidden. Provide “All permitted namespaces”, selected namespaces and manually entered namespaces, with accurate partial-result wording. An all-namespace 403 must not prevent reading an explicitly permitted namespace. Do not infer authorization from an empty list. [Lens cluster settings](https://docs.lenshq.io/k8slens/cluster/cluster-settings/).

The reference versions matter. The official Lens repository says its open-source edition is retired. The OpenLens binary release verified during research is `v6.5.2-366`, dated June 30, 2023. Treat it as historical interaction/source evidence. Independently implement the workflows; do not assume current proprietary Lens behavior exists in that source. [Lens repository](https://github.com/lensapp/lens), [OpenLens release](https://github.com/MuhammedKalkan/OpenLens/releases/tag/v6.5.2-366).

### 2. C# client and supporting library decision

| Candidate | Verified baseline | Fit for Asura |
| --- | --- | --- |
| `KubernetesClient.Aot` | 19.0.2; .NET 8/9/10; Apache-2.0 | Preferred starting point. Generated models/APIs and AOT serialization, with significant watch, unknown-resource and YAML gaps to prove. |
| `KubernetesClient` | 19.0.2; .NET 8/9/10; Apache-2.0 | Richer generic/watch convenience. Consider only if the actual backend packaging and analyzer boundary can use it without weakening policy. |
| `KubeOps.KubernetesClient` | 13.2.1; .NET 8/9/10; Apache-2.0 | Maintained operator-oriented wrapper over the ordinary client. Adds Abstractions/Transpiler; little benefit to this desktop browser. |
| `KubeClient` | 3.1.1; includes .NET Standard 2.1 and .NET 10; MIT | Dynamic resources, reactive streams and exec support are useful. Newtonsoft-based serialization and no verified Native AOT guarantee make it a weaker initial choice. |
| `kubectl` and `helm` | Versioned executable capabilities | Use for an explicit context-bound terminal and Helm workflows when needed. Do not make unstructured CLI table parsing the data model. |

Published package metadata and tagged source support this comparison. Recheck versions when implementation begins; repository HEAD is not a release. No package installation or version change is part of this planning session. [Official AOT package](https://www.nuget.org/packages/KubernetesClient.Aot/19.0.2), [KubeOps package](https://www.nuget.org/packages/KubeOps.KubernetesClient/13.2.1), [KubeOps dependency](https://github.com/dotnet/dotnet-operator-sdk/blob/v13.2.1/src/KubeOps.Abstractions/KubeOps.Abstractions.csproj), [KubeClient package](https://www.nuget.org/packages/KubeClient/3.1.1).

The AOT package is not a drop-in equivalent to the ordinary client. At tag `v19.0.2`, its compile list omits `GenericClient` and comments out `Watcher`/`WatcherExt`; generated watch convenience methods are guarded by `!K8S_AOT`. Plan a bounded discovery/list/watch adapter using JSON documents only if the spike confirms this is the smallest compliant solution. Keep unknown CRDs as data; do not generate C# assemblies or types at runtime. [AOT project](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/KubernetesClient.Aot/KubernetesClient.Aot.csproj), [watch generator](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/LibKubernetesGenerator/templates/OperationsExtensions.cs.template).

Arbitrary YAML needs separate proof. The AOT helper is internal and oriented toward kubeconfig, so its presence does not establish public arbitrary-resource serialization. Evaluate the SDK's YamlDotNet dependency at its exact resolved version, using bounded node/event parsing and JSON conversion, before adding another YAML library. Preserve unknown fields, scalar meaning and multi-document boundaries; reject duplicate-key ambiguity, excessive depth and oversized alias expansion. Comments and formatting need a documented preservation policy. Do not turn a JSON round trip into a claim of lossless YAML editing. [AOT YAML source](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/KubernetesClient.Aot/KubernetesYaml.cs).

The SDK supplies WebSocket operations for exec, attach and port forwarding, but not Asura's terminal lifecycle, local listener ownership or consumer routing. HTTP success is insufficient evidence for WebSocket success. Verify stream negotiation, resize, exit status, cancellation and TLS under each backend placement. Reuse existing terminal and editor controls; choose a new UI dependency only after checking those controls against YAML and large-log requirements. [SDK WebSocket implementation](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/KubernetesClient/Kubernetes.WebSocket.cs).

Asura's Linux backend is currently published self-contained with `PublishAot=false` and `PublishTrimmed=false`, while first-party source projects inherit `IsAotCompatible=true`. The macOS desktop has a Native AOT release path. These are different facts. The spike must evaluate the actual dependency reachability and both shipping configurations before selecting the AOT package or a smaller compliant worker-only ordinary-client arrangement. Relevant files are `Directory.Build.props`, `scripts/build-workspace-backend.sh` and `docs/macos-packaging.md`.

### 3. Connection, execution and session architecture

Introduce `KubernetesConnectionProfile` as an `IDurableDefinition`, with a typed profile ID and a new `DefinitionKind`. Store display metadata, explicit context, source mode, default namespace, optional namespace restrictions, credential references and optional saved SSH hop. Distinguish a linked kubeconfig from an imported managed configuration. Linked changes must be revalidated before replacing active authority. Managed credentials belong in the vault; portable profiles contain no tokens, private keys or executable trust grants.

Add a typed `KubernetesPanelTarget` to saved-panel and runtime-recovery contracts rather than encoding kubeconfig or cluster credentials in `Startup.Location`. It binds profile ID, namespace selection and initial resource view. Definitions may refer to a profile; a session binds an immutable resolved authority generation. Track profile/context identity, API server/TLS identity, credential generation and route generation. Pod references also carry namespace, GVR, UID and container identity as applicable. A same-name replacement must not inherit an old reference or approval.

| Owner | Proposed responsibility |
| --- | --- |
| `Asura.Core` | Durable profiles, target IDs, validation and policy capabilities; BCL only. |
| `Asura.Application` | Owned DTOs and typed session/factory, observation, mutation, exec and forward contracts. |
| `Asura.Kubernetes` | SDK adapter, API discovery, projections, watch reduction, credential integration and Kubernetes semantics. SDK types remain private. |
| `Asura.ConnectionBackend` / `Asura.Backend` | Dedicated Kubernetes session protocol and worker mode; bounded authenticated private IPC, credentials and streams. |
| `Asura.SessionHost` | Workspace/session ownership, graph references, attachments, revisions, action authorization and lifecycle. |
| `Asura.Desktop` | Workspace-specific factory registration, execution placement, route authority, vault and terminal composition. |
| `Asura.App` | View models/views, commands, preferences and error presentation through application contracts. |

Follow ADR 0056: non-isolated Direct uses an owned host child; isolated workspaces use their guest; custom-network or explicit SSH-hop connections use the supported private service backend. Capability-check platform availability before connecting. Run Kubernetes HTTP, WebSocket traffic, authentication helpers and auxiliary CLI traffic in the selected execution environment. A route failure retires that generation. It cannot silently move work to host networking or reuse a pool under replacement authority.

Prefer a dedicated Kubernetes backend protocol, modeled on the existing session-lived Redis/file workers. The generic HTTP bridge carries bounded responses but not complete client-certificate configuration or duplex WebSocket channels, and caps a response at 128 MiB. Avoid increasing that global limit to accommodate indefinite watches. Kubernetes streams need individually bounded frames, queue limits, explicit cancellation/close, heartbeats or progress detection, and resynchronization. Initially use one worker per panel/session; do not introduce cross-panel connection pooling until measured demand justifies the additional authority and lifetime rules.

Give each independently usable forward or detached exec its own workspace-owned backend session and immutable authority lease. The main browser worker can then close or rebind without killing detached consumers. Inline shells follow their parent unless explicitly detached; moving a view alone is not detachment. Forward consumers retain the forward session until explicit Stop or the last owner closes. Route revocation and workspace shutdown override all such leases. This extends the Docker precedent, whose current inline children are disposed with the parent view model.

Define named operations such as discover, list, inspect, watch, read logs, prepare mutation, execute mutation, open exec and start/stop forward. Never add an arbitrary URL/verb/body tool that bypasses resource authorization. Include protocol version, request/session IDs, target generation, deadlines, size budgets, ordered sequence and mutation dispatch state. A future headless client can consume the same application contracts without receiving SDK objects or UI handles.

Import is a parse-and-review operation. It must not call an SDK configuration builder that immediately executes credential plugins. Kubernetes explicitly warns that kubeconfigs can execute code or expose files; the reviewed SDK's configuration path starts external processes and has a certificate-refresh limitation. Resolve approved `exec` commands with argument arrays in the selected backend; bound runtime/stdout, coordinate refresh, implement `ExecCredential` v1/v1beta1 semantics and expose interactive-login-required state. Never send credential output to the agent. A plugin being installed on macOS does not mean it exists inside a Linux workspace. [Kubeconfig warning](https://kubernetes.io/docs/concepts/configuration/organize-cluster-access-kubeconfig/), [credential protocol](https://kubernetes.io/docs/reference/access-authn-authz/authentication/), [SDK credential source](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/KubernetesClient.Aot/KubernetesClientConfiguration.ConfigFile.cs).

Kubeconfig `proxy-url`, environment proxies, TLS server-name overrides, relative certificate/token files and credential-provider egress need explicit resolution rules. Compose only supported proxy/hop arrangements with the selected workspace route; reject conflicting configurations with an actionable explanation. Preserve original API TLS identity through a tunnel. Do not silently inherit insecure TLS settings or ambient host credentials. The newer kubectl credential-plugin allowlist is not a C# client guarantee. Asura must own its review and execution policy. [Kubeconfig schema](https://kubernetes.io/docs/reference/config-api/kubeconfig.v1/), [kubectl allowlist](https://kubernetes.io/blog/2026/01/09/kubernetes-v1-35-kuberc-credential-plugin-allowlist/).

### 4. Resource browsing, debugging and mutation semantics

Discover served API groups, versions, scope and verbs. Render useful built-in summaries while allowing any discovered resource through a generic table and inspector. CRDs should use server Table responses where available, then bounded metadata/condition columns as fallback; use CRD printer columns only when the user can read the definition. Listing CRDs must not be a prerequisite for listing an already discoverable custom-resource endpoint. Legacy OpenLens demonstrates printer-column-based views; the actual capability comes from Kubernetes discovery and representation negotiation. [Legacy CRD view](https://github.com/lensapp/lens/blob/master/packages/core/src/renderer/components/custom-resources/view.tsx), [API concepts](https://kubernetes.io/docs/reference/using-api/api-concepts/).

| Resource family | Required useful coverage |
| --- | --- |
| Workloads | Pods, Deployments, StatefulSets, DaemonSets, ReplicaSets, Jobs, CronJobs; readiness, restarts, owner links and rollout state. |
| Network | Services, EndpointSlices, Ingresses/IngressClasses, NetworkPolicies; Gateway API through discovery; port forwards. |
| Configuration | ConfigMaps, metadata-only Secrets, HPA, resource quotas, limits and disruption budgets. |
| Storage | PVCs, PVs, StorageClasses; claims, binding state and related workload links. |
| Cluster/access | Nodes, namespaces, service accounts, Roles/ClusterRoles and bindings, subject to permissions. |
| Extensions | Custom resources grouped by API group, discovered served versions and scope; generic YAML/conditions fallback. |
| Operations | Events, active forwards, Helm releases, optional metrics. Additional APIs appear through generic discovery. |

Use list-then-watch with collection resourceVersion and bounded pagination. On expired history, discard the affected cache, relist and resume. Treat resourceVersion as an opaque token, separate from Asura's event sequence. Watch reconnect can retry reads with backoff; a possibly dispatched mutation cannot use that retry policy. Respect 429/Retry-After, distinguish forbidden access from transport failure, and mark stale data while reconnecting. Subscribe only to active kinds, scopes and selected-resource relationships. An inactive view should release unnecessary watches. [Kubernetes watch semantics](https://kubernetes.io/docs/reference/using-api/api-concepts/).

Proposed initial budgets, to be confirmed by measurement: list pages of 200, at most 5,000 cached rows per active collection, 10,000 visible log lines or 8 MiB of log text, and bounded per-document/frame sizes agreed in the protocol. Exceeding a budget produces explicit truncation, paging or resynchronization, never silent omission. Search and sort must say whether they cover loaded rows or the full server-filtered result. Coalesce updates before posting them to Avalonia. Large clusters must not cause a full resource-graph refresh on every event.

Logs need pod/container selection, previous-container mode, tail/since controls, timestamps, follow/pause, wrapping, search versus filtering, and bounded export. Label any reconnect gap and identify pod UID/container restart boundaries; Kubernetes logs are not a durable log archive. Controller-wide aggregation can follow single-pod support, with explicit per-pod attribution and fan-out limits. These choices build on Lens's documented log controls. [Lens log options](https://docs.lenshq.io/k8slens/using-lens/log-options/).

Exec must reuse the existing terminal state/render/input system through a new transport adapter, not emulate a terminal with a text box. Bind namespace, pod UID, container and command. Handle TTY resizing, UTF-8, stderr differences with TTY, exit status, cancellation and human preemption of agent input. A missing shell in a distroless image is an unsupported command, not a reason to inject a debugging container automatically. A local context-bound kubectl terminal is a separate action with broader command authority. It must not modify global kubeconfig state or masquerade as typed Kubernetes authorization.

Exec/attach/port-forward endpoints are addressed by pod name and do not accept mutation-style UID preconditions. Check the expected UID immediately before opening, retire streams when replacement is observed, and do not claim atomic UID fencing across that request. A residual name-replacement race remains; the stage-0 threat/behavior decision must document it and define which agent operations are available under that limitation. Resource-mutation preconditions provide a stronger guarantee where supported.

Port forwards are workspace-owned runtime objects with independent IDs and explicit consumer leases. Starting one records context, namespace, resolved pod UID, remote port and requested/assigned local port. Bind loopback by default, TCP only, with pending/running/failed/stopped states and explicit stop. Service forwards resolve a suitable backing pod and make replacement behavior visible. Closing the inspector must not stop a forward still used by a browser/database panel. Closing the last owning session/workspace closes it; explicit Stop terminates all consumers with a useful explanation. Lens's central list is a useful interaction precedent. [Lens port forwarding](https://docs.lenshq.io/k8slens/cluster/use-port-forwarding/).

A guest listener is not desktop localhost. Browser/database integration must obtain a scoped endpoint usable in that consumer's execution location. Implement the necessary private relay and lease through existing backend control channels; test guest-to-host and backend-to-backend paths, original application TLS identity, port collisions and route revocation. Never turn a forward into an unrestricted loopback bypass. The forwarded app may use HTTPS or database TLS even when the Kubernetes tunnel is already encrypted; preserve that second layer's intended host verification.

File browsing/copy is a separate capability attached to a pod/container. Reuse File Viewer and transfer UI only after defining allowed operations, path rules and command/archive prerequisites. Many images lack a shell or tar; Kubernetes documents tar as a prerequisite for `kubectl cp`. Support a clear unavailable state and preserve existing transfer cancellation, mutation approvals and traversal protections. Never silently install utilities in a workload. [kubectl copy](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_cp/).

YAML editing starts from a loaded baseline. Preserve a dirty draft when watch updates arrive; show a diff, run server dry-run, display admission/defaulting results and submit an explicitly reviewed change. Editor Revert only discards local edits, as Lens also documents. For existing objects, prefer a minimal patch with UID/resourceVersion preconditions. For declarative create/apply, use an explicit field manager and show managed-field conflicts; force ownership requires a separate choice. Dry-run validates a point in time, not a reservation. Revalidate the target and approval after relevant changes. A multi-document operation reports per-object results and partial completion; it is not a transaction. [Lens editor](https://docs.lenshq.io/k8slens/using-lens/advanced-editor/), [server-side apply](https://kubernetes.io/docs/reference/using-api/server-side-apply/).

Typed mutations should include scale, rollout restart, delete, create Job from CronJob and suspend/resume CronJob. Node cordon/uncordon, eviction and drain require their own workflows, respecting disruption budgets, timeouts and partial progress. Force deletion/finalizer removal and broad RBAC changes need explicit impact displays. Inform users when a controller or GitOps manager may revert their edit. If a connection fails after dispatch, show outcome unknown, inspect the resource, and never blindly repeat the write.

Secrets start as metadata. Key names require an authorized Secret data read and backend-side removal of values; metadata-only API responses cannot provide keys. Reveal/copy/edit is separately controlled, time-bounded presentation. Do not emit plaintext secret values into tool results, recovery, diagnostics or ordinary exports. Secret protection also includes credentials embedded in arbitrary ConfigMaps, custom resources, log text or annotations: schema-based redaction cannot prove that arbitrary content is safe. Agent reads of resource bodies and logs require explicit data authority, bounded output and existing sensitive-content handling.

Metrics are optional capabilities. Kubernetes Metrics API supplies basic CPU/memory usage when available; Prometheus enables a separate historical view. Distinguish unsupported, forbidden and stale data, and avoid displaying absence as zero. Do not install metrics components merely to open the panel. Query Prometheus through the same route/authentication rules. [Kubernetes metrics](https://kubernetes.io/docs/tasks/debug/debug-cluster/resource-metrics-pipeline/), [Lens metrics integration](https://docs.lenshq.io/k8slens/cluster/cluster-metrics/).

Helm should use a bounded, explicitly provisioned CLI adapter in the selected backend rather than a new C# Helm implementation. Begin with release listing/details/history; add install, upgrade, rollback and uninstall with namespace/version/values review and output limits. Charts, hooks, plugins, repository authentication and OCI downloads add execution and network behavior beyond an ordinary API patch. Preview output can itself contain secrets. Keep those permissions and redaction rules explicit, disable ambient plugins unless reviewed, and show resources that remain after uninstall. [Lens Helm workflow](https://docs.lenshq.io/k8slens/how-to/manage-helm-charts/), [Helm list](https://helm.sh/docs/helm/helm_list/).

### 5. Every surrounding Asura integration

This matrix identifies existing code seams, not exact future class signatures. Extend the working patterns narrowly. The panel is incomplete until these integrations are covered.

| Feature | Existing files / precedent | Required change |
| --- | --- | --- |
| Panel identity | `src/Asura.Core/PanelKind.cs`, `ScreenPanelKind.cs`; `RuntimeWorkspaceRecovery.cs` | Append persisted enum values. Update kind maps, labels and unsupported-version behavior. |
| Saved connection | `DatabaseConnectionProfile.cs`, `DefinitionKind.cs`; `UnifiedConnectionEditorViewModel.cs` | New Kubernetes profile family, editor result, create/edit/test/connect-once flows and vault references. |
| Launcher/chooser | `MainWindowViewModel.cs`, `KindBadges.cs`, `IPanelLaunchCapabilitySource.cs`, `NewPanelChooserView.axaml` | Search/open profile, new tab/panel, placeholder choice, split and rebind with accurate capability reporting. |
| Screens/workspace entries | `ScreenPanelDefinition.cs`, `ScreenValidator.cs`, `WorkspaceEntry.cs`; `SavedScreenEditorViewModel.cs`, `WorkspaceTabPanelEditorViewModel.cs` | Typed Kubernetes binding, dependency validation and missing-profile repair; saved-screen startup contains view intent only. |
| Autosave/recovery | `WorkspaceAutoSaveCoordinator.cs`, `RuntimeWorkspaceRecovery.cs` | Save profile/context/view/filter/columns and safe selection. Reconnect reads only; never replay exec, forwards or mutations. |
| Import/export | `DefinitionJson.cs`, `SqliteDefinitionBundleStore.cs`, `PortableDefinitionBundle.cs` | Source-generated JSON, schema migration, dependency closure, credential detachment and frozen execution review for plugin specs. |
| Session host | `IPanelSession.cs`, `ISessionHostClient.cs`, `InMemorySessionHostClient.Docker.cs` | Typed ensure-session, immutable bindings, revisioned snapshots/events, close/cancel and resource cleanup on rejected registration. |
| Composition | `DesktopComposition.cs`, `DesktopWorkspaceRuntimeServicesFactory.cs`, `WorkspaceRuntimeServices.cs`, `WorkspaceDockerPanelSessionFactory.cs` | Per-workspace Kubernetes factory/backend registration for every supported execution mode; no fallback registration. |
| Network/backend | `WorkspaceConnectionBackendFactory.cs`, `WorkspaceNetworkRouteSnapshot.cs`, `ConnectionBackendCommand.cs`, `WorkspaceHttpProtocol.cs` | Owned Kubernetes mode, route capture/revocation, stream IPC, credential callbacks and scoped forward endpoints. |
| Credentials | `ISecretVault.cs`, `SecretScopeKind.cs`, `SecretUseKind.cs`, `SecretScopeAccessPolicy.cs`, `ConnectionCredentialProcessHost.cs` | Add Kubernetes profile scope/authentication purpose, scoped callbacks and owner references; reuse trust/process boundaries, backend-visible paths, refresh and login flow. |
| Panel presentation | `DockerRuntimePanelViewModel.cs`, `DockerRuntimePanelView.axaml`, `PanelChrome.cs`, `HostedPanelSessionLink.cs` | Native responsive view; dock/float/maximize/move without reconnecting; preserve focus and dirty-editor close guards. |
| Cross-panel operations | Docker inline terminal/File Viewer sessions; browser/database creation | Independent hosted child IDs, exact target metadata and consumer lifetime dependencies; no copied credentials in new-panel URLs. |
| Commands/settings | Existing command/keymap catalog and settings view models | Contextual resource actions, reconnect, logs, shell, forward and search; collision-free keybindings and per-profile/kind preferences. |
| Notifications | `IPanelSession.WatchNotificationsAsync`, `IPanelNotificationSource.cs`, `ShellNotificationCenter*` | Actionable auth failure, watch loss, forward stop, operation completion; deduplicate storms and honor existing delivery settings. |
| Privacy/data controls | Recovery/diagnostics inventory and secret-safe projections | No raw kubeconfig, logs, manifests or secret bodies in crash snapshots; bounded in-memory caches and explicit exports. |
| Agent policy | `AgentPolicy.cs`, `StoredAgentPolicyJson.cs`, `AgentPolicyPresentation.cs` | New capabilities, default-off normalization, settings/screen/checkpoint migrations and consistent policy displays. |
| Agent tools/context | `AgentToolCatalog.cs`, `AgentContextPanel.cs`, `AgentContextBindingFingerprint.cs`; Docker runtime/composer/host counterparts | Bounded discovery/read tools, typed mutations, exact authority binding, tool schemas/parser/results and context projection. |
| Agent-created panels | `MainWindowViewModel.AgentWorkspaceLayout.cs`, `InMemorySessionHostClient.AgentPanels.cs` | Advertise Kubernetes targets and create/bind panels separately from granting cluster access. |
| Build/release | `Asura.slnx`, `Directory.Packages.props`, lock files, managed-component catalogs and notices | SDK/transitive audit in correct desktop/backend closures, platform locks, optional CLI artifact verification and release evidence. |

Restored selections must tolerate deleted objects and changed contexts. A missing profile or kubeconfig gets a repair state; it must not silently choose another context. Persist editor drafts only through a separately reviewed confidential draft store, or explicitly offer export/discard on close; ordinary recovery is not a place to persist raw YAML. A workspace can restore panel geometry and safe view intent without restoring live network authority.

Add a `KubernetesConnection` secret scope and `KubernetesConnectionAuthentication` use purpose, including the explicit `SecretScopeAccessPolicy.ScopeFor` mapping and profile ownership checks. Existing shell `ConnectionAuthentication` is not authority for this new profile family. Test that a worker for one cluster profile cannot resolve another profile's secret, and that copied/imported profiles do not inherit secret-use authority.

The existing `AgentPolicy.IsStructurallyValid` checks the complete capability set. Adding enum entries without migration can invalidate older policies. Propose `KubernetesData` and `KubernetesControl`, plus a distinct `KubernetesSecrets` grant if secret reveal is exposed. Exec additionally requires existing command authority, file mutations require the corresponding file capability, and forwards require network/control authority with endpoint scope. Record the exact capability composition in the first ADR. All new grants default Off on migration.

Human and agent operations must reach the same application/session path. Agent observations return bounded structured summaries and opaque references. A mutation approval binds workspace/session revision, authority generation, namespace, GVR, UID, subresource and reviewed payload. Revalidate immediately before dispatch, consume authorization once, and reject a changed target. Kubernetes SelfSubjectAccessReview informs action availability; the actual API response remains authoritative because permissions can change. UI namespace filtering is not an authorization boundary. Resource text and logs remain untrusted data, never instructions for the agent. [Kubernetes authorization](https://kubernetes.io/docs/reference/access-authn-authz/authorization/).

## Synthesis and simplification decisions

The best fit is one native panel, one dedicated profile type and one owned Kubernetes backend boundary. The breadth comes from resource discovery and existing Asura services. It should not require a second workspace manager, a new terminal engine, a cloud account or generated code for every CRD.

Recommended simplifications, within this planning brief:

- **S1, preferred:** reuse the official client in proposed `Asura.Kubernetes` API operations, subject to the spike; avoid hand-written typed APIs, transport negotiation and model generation.
- **S2:** derive resource navigation/columns from discovery in the proposed resource catalog; avoid individual list/watch implementations and panel types for every CRD.
- **S3:** reuse `DockerRuntimePanelViewModel`'s separate hosted terminal/File Viewer ownership pattern; avoid a Kubernetes-specific terminal renderer and duplicate transfer UI.
- **S4:** reuse `WorkspaceConnectionBackendFactory` execution placement and credential services; avoid another proxy/VPN/SSH implementation inside the Kubernetes module.
- **S5:** begin with per-panel ownership; avoid a new cross-workspace connection pool, global cluster cache or generic plugin framework before there is measured need.

These are recommendations, not changes to existing code. Sharing visual controls is appropriate where their behavior already matches; extracting a universal “resource panel framework” from Docker, Git and Kubernetes is not a prerequisite.

## Recommendations and implementation sequence

The beads epic `asura-dp53` contains the following tasks. Later stages depend on the recorded foundations. Debugging and typed mutations can proceed independently after the read-only/session/governance work. The final task depends on both.

| Stage / issue | Deliverable and exit criteria |
| --- | --- |
| 0 / `asura-dp53.1` | SDK/backend/authentication spike and ADR. Publish/run the actual backend and desktop dependency paths. Prove typed and unknown-resource list/watch, arbitrary YAML, logs, exec resize/exit and multi-connection forwarding. Exercise token/mTLS/exec refresh and route revocation. Choose package and transport only after evidence. |
| 1 / `asura-dp53.2` | Durable profiles, connection editor and safe import. Open explicit contexts from linked/managed configs, preserve merge/path semantics, test namespace-restricted users, vault ownership, import review and migration/export round trips. |
| 2 / `asura-dp53.3` | Hosted read-only browser. Discover, list/watch, inspect, events, optional live metrics and unknown CRDs through bounded backend IPC. Handle partial access and stale data; show responsive native UI. |
| 3 / `asura-dp53.4` | Complete shell and read-agent integration. All launcher/screen/layout/recovery/notification paths work; migrated policy is valid and Off for new grants; exact hosted references and bounded tools pass contract tests. |
| 4 / `asura-dp53.5` | Logs, exec and port forwards with browser/database/File Viewer integration. Prove input arbitration, namespace/pod/container identity, missing-tool states, scoped consumer endpoints and cleanup. |
| 5 / `asura-dp53.6` | Governed changes. YAML preview/dry-run/diff, basic typed operations and node workflows, concurrency/UID checks, field conflicts, partial and unknown outcomes, equivalent human/agent behavior. |
| 6 / `asura-dp53.7` | Operational breadth and release. Helm, optional Prometheus history, advanced admin polish, dependency notices/locks, real-cluster/platform acceptance and documented supported matrix. |

Stage 0 is a decision gate, not permission to weaken warnings. If the AOT package requires too much bespoke implementation, compare the ordinary client in a properly isolated worker against the repository's real analyzer and distribution constraints. Record why the chosen path is smaller and compliant. If neither passes, revise the dependency decision before committing to the UI/backend contract. Do not ship a partial feature that silently bypasses workspace routing.

The first internal usable milestone is stage 3. It is not full operational completion. The requested complete panel includes stages 4–6 and all corresponding surrounding integrations. Stage 6 may add richer historical monitoring after the core browser is usable, but unknown CRD browsing and namespace-limited access are foundational, not optional polish.

### Acceptance strategy

Use deterministic fake API/worker fixtures for protocol and failure semantics, plus opt-in disposable real clusters for transport and interoperability. Keep live cloud accounts out of the default quality gate. Set the supported Kubernetes matrix from actual tests against release-pinned clusters; do not infer it from the client's generated-model version or the latest documentation website. Include built-in resources and at least two custom resources with different scope/schema/versions.

Core and infrastructure tests cover enum stability, profile validation, source-generated serialization, old schema/policy upgrades, portable import with detached credentials, missing references and recovery without live authority. Architecture tests enforce BCL-only Core, no SDK types in UI/application contracts, no host-network fallback and correct worker placement. New projects likely include `Asura.Kubernetes.Tests` and an opt-in integration suite, with existing App/SessionHost/Architecture/Agent suites extended where their behavior changes.

Transport tests must cover paginated list consistency, expired watch history, duplicate events, bookmarks, deleted CRDs, 401 refresh, 403, 429, 5xx, stalled streams, cancellation and bounded backlog. Authentication fixtures include token files, client certificates, rotated credentials, exec v1/v1beta1, spaces in arguments, missing binaries, interactive login and malicious imported configs that must not execute during preview. Validate API and helper egress separately under Direct, workspace isolation, custom proxy/VPN and SSH-hop routes.

The decisive end-to-end flows are:

1. Import two contexts and open panels side by side. Scope, selection, updates and credentials remain independent. Changing global kubeconfig current-context does not retarget either panel.
2. Find an unhealthy Deployment, follow owner/pod links, inspect events, read previous-container logs and open a container terminal. A missing shell gives a useful error.
3. Browse with access to one namespace and no namespace-list permission. Other denied resources and missing metrics do not block permitted work.
4. Inspect an unknown custom resource with columns/conditions, then remove its served version. The panel explains the change and refreshes discovery without crashing.
5. Edit YAML, compare, dry-run and apply. An external edit or same-name replacement invalidates the stale operation. A lost response produces outcome unknown and no blind replay.
6. Forward a service, open a browser and a database consumer where applicable, close the inspector and close/rebind the originating Kubernetes panel, then stop the forward. Detached consumers retain their original authority and usable forward across host/guest boundaries; inline-only children close with their parent.
7. Revoke the route while watch, logs, exec and forward are active. All old authority ends, no direct connection occurs, and reconnect creates a new generation.
8. Restore a workspace with a missing kubeconfig, expired plugin credentials and deleted selected pod. Geometry/view intent returns, commands and forwards do not replay, and sensitive data is absent from snapshots.
9. Agent reads and actions obey Off/Ask/Auto/YOLO semantics as applicable to existing policy, exact scope and cancellation. A context switch while approval is pending rejects the stale action.

Design QA should add loading, no resources, no selection, partial discovery, forbidden, stale, reconnecting, missing metrics, dirty editor, conflict, terminating resource and forward failure scenes. Verify keyboard-only navigation and real screen-reader behavior on the supported OS matrix; screenshots alone are insufficient. Measure a synthetic large-resource fixture for bounded allocations, stable selection and responsive input, then set justified performance thresholds.

For implementation handoff, run `./scripts/check.sh --full` with the repository SDK pinned in `global.json`. Keep NuGet versions in `Directory.Packages.props`, refresh all relevant lock graphs, and update both desktop and workspace-backend dependency catalogs where affected. Bundled kubectl/Helm requires architecture-specific provenance, checksums, licenses and update policy. Existing host tests do not prove a packaged Linux guest or macOS Native AOT release.

## Limitations and counterevidence register

| Finding that limits the recommendation | Consequence |
| --- | --- |
| AOT branding does not imply feature parity; released watch/generic/YAML conveniences differ. | Dependency choice stays conditional until stage 0 passes. |
| Backend release currently disables AOT/trimming, while first-party projects still assert AOT compatibility. | Compare actual shipping graphs instead of assuming every worker is AOT or every worker is exempt. |
| Current Lens documentation and historical OpenLens are different products/versions. | Use documented workflows and independently implemented patterns; avoid claiming source parity. |
| Most library capability claims have one authoritative implementation/package source, not three independent confirmations. | High confidence in what source contains; lower confidence in real-world interoperability until tested. |
| No real cluster, cloud login or network-path experiment was run. | Transport, authentication, supported version matrix and performance remain implementation acceptance work. |
| API bodies/logs can contain credentials outside Kubernetes Secret objects. | Redaction alone cannot make unrestricted agent observation safe. Bound and govern content access. |

No performance benchmark, staffing estimate or delivery date is asserted. The feature breadth is known; the cost of AOT adaptation, backend streams and cross-location forwards is not yet measured. Those three results should determine the implementation estimate. The plan also leaves vendor-specific cloud auto-discovery, Lens collaboration/accounts, cluster provisioning, vulnerability scanners, a Lens extension marketplace and fleet-wide search outside this epic. Existing Asura agents and MCP remain available through their established boundaries.

## Claims-evidence table

| Decision-driving claim | Evidence | Confidence |
| --- | --- | --- |
| Lens provides resource navigation, tables, details and operational views. | Official layout, Pods, logs, editor and forwarding documentation; official layout image inspected. | High for documented UX, untested live behavior. |
| AOT client lacks standard watch/generic convenience. | Released `v19.0.2` project compile list and generated extension guards. | High for source; adaptation cost unmeasured. |
| Workspace execution placement is mandatory. | Accepted ADR 0056 and `WorkspaceConnectionBackendFactory`. | High for repository architecture. |
| Existing HTTP backend is insufficient as an assumed full stream transport. | `WorkspaceHttpProtocol` and `WorkspaceHttpMessageHandler`; absent certificate/WebSocket contracts. | High for current code. |
| Saved-policy changes require normalization. | `AgentPolicy.IsStructurallyValid` and `StoredAgentPolicyJson`. | High for current code. |
| A dedicated native panel can reuse existing Asura services. | Docker/session/profile precedents plus the integration matrix. | Design inference, to validate through implementation. |

## Methodology appendix

The scope was divided into UX, client/library behavior and repository integration. Primary documentation, release metadata and tagged source were preferred over third-party tutorials. Searches used the available web tool because the research skill's standalone search CLI was unavailable. Independent research passes examined Lens/OpenLens, C# libraries and Asura integration; their findings were reconciled against current code and the release scripts. Specific product/package facts are not padded with unrelated sources to simulate independent corroboration.

The outline changed after two findings: the AOT client's missing conveniences and the workspace backend's transport/placement constraints. Both moved ahead of UI implementation. Current documentation can discuss versions beyond the intended test matrix, so no server-version support claim was derived from a documentation heading. Published package baselines were distinguished from repository HEAD.

Visual evidence was the official Lens layout image, inspected in a browser, and existing Asura screenshots `workspace-docker-logs.png` and `dialog-connection-editor.png`. Source inspection supplied behavior that screenshots cannot prove. No screenshots from a live connected cluster were produced.

The local companion research folder is `/Users/terion/Documents/Asura_Kubernetes_Research_20260917/`. It contains the report, source registry, evidence spans, claim ledger and run manifest. The repository document is the portable planning reference; beads owns implementation status and dependencies. Research output and the implementation backlog are distinct from implemented product behavior.

## Bibliography

All sources retrieved 2026-09-17. Upstream pages without a stable publication date are identified by their retrieval date; source links with `v19.0.2` or `v13.2.1` are release-pinned.

[1] Kubernetes. "Kubernetes API Concepts". [Documentation](https://kubernetes.io/docs/reference/using-api/api-concepts/).

[2] Kubernetes. "Server-Side Apply". [Documentation](https://kubernetes.io/docs/reference/using-api/server-side-apply/).

[3] Kubernetes. "Organizing Cluster Access Using kubeconfig Files". [Documentation](https://kubernetes.io/docs/concepts/configuration/organize-cluster-access-kubeconfig/).

[4] Kubernetes. "Authenticating". [Documentation](https://kubernetes.io/docs/reference/access-authn-authz/authentication/).

[5] Kubernetes. "Authorization". [Documentation](https://kubernetes.io/docs/reference/access-authn-authz/authorization/).

[6] Kubernetes. "Resource metrics pipeline". [Documentation](https://kubernetes.io/docs/tasks/debug/debug-cluster/resource-metrics-pipeline/).

[7] Kubernetes. "kubectl cp". [Documentation](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_cp/).

[8] Kubernetes (2026). "Restricting executables invoked by kubeconfigs via exec plugin allowList". [Project article](https://kubernetes.io/blog/2026/01/09/kubernetes-v1-35-kuberc-credential-plugin-allowlist/).

[9] Lens. "Lens K8S IDE layout". [Documentation](https://docs.lenshq.io/k8slens/using-lens/layout/).

[10] Lens. "Pods view". [Documentation](https://docs.lenshq.io/k8slens/using-lens/workloads/pods/).

[11] Lens. "Cluster settings". [Documentation](https://docs.lenshq.io/k8slens/cluster/cluster-settings/).

[12] Lens. "Log options". [Documentation](https://docs.lenshq.io/k8slens/using-lens/log-options/).

[13] Lens. "Port forward traffic". [Documentation](https://docs.lenshq.io/k8slens/cluster/use-port-forwarding/).

[14] Lens. "Advanced editor". [Documentation](https://docs.lenshq.io/k8slens/using-lens/advanced-editor/).

[15] Lens. "Manage Helm charts". [Documentation](https://docs.lenshq.io/k8slens/how-to/manage-helm-charts/).

[16] Lens. "Lens repository". [Project README](https://github.com/lensapp/lens).

[17] OpenLens (2023). "v6.5.2-366". [Release](https://github.com/MuhammedKalkan/OpenLens/releases/tag/v6.5.2-366).

[18] Kubernetes client maintainers (2026). "KubernetesClient.Aot 19.0.2". [NuGet](https://www.nuget.org/packages/KubernetesClient.Aot/19.0.2).

[19] Kubernetes client maintainers. "AOT project at v19.0.2". [Tagged source](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/KubernetesClient.Aot/KubernetesClient.Aot.csproj).

[20] Kubernetes client maintainers. "AOT YAML helper". [Tagged source](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/KubernetesClient.Aot/KubernetesYaml.cs).

[21] Kubernetes client maintainers. "AOT kubeconfig and credential execution". [Tagged source](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/KubernetesClient.Aot/KubernetesClientConfiguration.ConfigFile.cs).

[22] Kubernetes client maintainers. "Kubernetes WebSocket implementation". [Tagged source](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/KubernetesClient/Kubernetes.WebSocket.cs).

[23] KubeOps maintainers (2026). "KubeOps.KubernetesClient 13.2.1". [NuGet](https://www.nuget.org/packages/KubeOps.KubernetesClient/13.2.1).

[24] KubeClient maintainers (2025). "KubeClient 3.1.1". [NuGet](https://www.nuget.org/packages/KubeClient/3.1.1).

[25] Lens. "Legacy custom resource view". [Historical source](https://github.com/lensapp/lens/blob/master/packages/core/src/renderer/components/custom-resources/view.tsx).

[26] Lens. "Add a local cluster". [Documentation](https://docs.lenshq.io/k8slens/getting-started/add-clusters/add-local-cluster/).

[27] Lens. "Enabling cluster metrics". [Documentation](https://docs.lenshq.io/k8slens/cluster/cluster-metrics/).

[28] Helm maintainers. "Helm list". [Documentation](https://helm.sh/docs/helm/helm_list/).

[29] Kubernetes client maintainers. "Operations extension generator at v19.0.2". [Tagged source](https://github.com/kubernetes-client/csharp/blob/v19.0.2/src/LibKubernetesGenerator/templates/OperationsExtensions.cs.template).

[30] KubeOps maintainers. "Abstractions dependencies at v13.2.1". [Tagged source](https://github.com/dotnet/dotnet-operator-sdk/blob/v13.2.1/src/KubeOps.Abstractions/KubeOps.Abstractions.csproj).

[31] Kubernetes. "kubeconfig v1 schema". [Documentation](https://kubernetes.io/docs/reference/config-api/kubeconfig.v1/).

Local evidence: `docs/adr/0056-workspace-database-backend.md`, `docs/architecture.md`, `Directory.Build.props`, `scripts/build-workspace-backend.sh`, `docs/macos-packaging.md`, and the source files listed in the integration and claims-evidence tables. All refer to the repository baseline stated above, with pre-existing working-tree edits left outside this planning change.
