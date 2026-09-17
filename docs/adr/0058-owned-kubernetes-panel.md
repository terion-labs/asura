# ADR 0058: Owned Kubernetes panel

Status: Accepted for development. Packaged guest and platform release acceptance remain separate gates.

## Decision

Use the official `KubernetesClient.Aot` 19.0.2 in `Asura.Kubernetes`, behind the existing owned connection backend. Its authenticated HTTP client supplies TLS and API transport. Bounded JSON discovery/list/watch operations support unknown resources without generated models. YamlDotNet 16.3.0 parses configuration and converts a single manifest to JSON; conversion preserves data, not YAML comments or formatting.

The backend process owns credentials, Kubernetes transports, watches and reviewed maintenance operations. Application contracts contain detached data and typed resource identities, with no Kubernetes SDK objects. Core stores a dedicated Kubernetes profile and view target. A profile selects one explicit context; changing kubeconfig `current-context` does not retarget a panel.

Reuse Asura's native panel layout, unified connection editor, terminal renderer, browser, database panel, File Viewer, hosted sessions and capability broker. No second workspace manager or global cluster cache is introduced. A panel owns its browser/watch lifetime. Detached terminals and forwards have independent workspace-owned lifetimes.

## Boundaries and invariants

- Configuration review only parses. It does not execute credential plugins or dereference certificate/key/token paths. Executing a kubeconfig helper requires the exact reviewed command/environment fingerprint. Linked files resolve relative paths at the backend. Imported definitions detach credential authority and start disabled.
- Authentication supports bearer tokens/token files, client certificates and approved exec helpers. Interactive-only helpers, legacy auth-provider configurations and kubeconfig proxy URLs produce explicit unsupported states. API and helper execution follow the selected workspace backend; there is no fallback to desktop networking when a route fails.
- Private framed IPC has a size ceiling, request sequence and response discriminator. Streams use bounded channels. Cancellation and route revocation terminate owned children and release leases. Mutation transport failures after dispatch return outcome unknown and are never replayed.
- A typed pod terminal carries namespace, pod UID, container and literal argv. Ordinary terminal creation rejects that target. Reuse rejects changed pod identities. Recovery restores a resource browser, never a running command.
- Container files use the existing read-only File Viewer through UID-bound non-TTY exec. Paths are literal command arguments; `/bin/sh` and `head` must exist in the container. Listings stop at 10,000 entries and previews at 1 MiB. No local file provider is substituted. File metadata unavailable from the portable commands stays unknown. Recovery and autosave restore the Kubernetes resource browser, without local file-provider authority or automatic container execution. These detached file panels are not agent file targets.
- Each forwarded TCP connection gets an owned backend relay. An independent anchor keeps route authority alive across consumer disconnects. Workspace-private host aliases map only an exact live host/port lease; revoked aliases never fall through to DNS. Browser/database consumers use that route. Database credentials and private endpoints are transient and never saved as durable profiles. Recovery restores forwarded browsers at `about:blank` and forwarded databases as unconfigured pickers; it does not restart forwards.
- Apply, JSON Patch, delete and scheduling changes check UID/resourceVersion and use server preconditions. UI commits require the exact reviewed request; changes invalidate review. Drain reviews are not atomic dry runs: blocked pods prevent dispatch, evictions respect PDBs, and partial/unknown results remain visible. Helm reviews are expiring, single-use, worker-local tokens. Upgrades require an immutable OCI digest and an exact chart version; release revision and storage-object identity are rechecked. Helm CLI execution still has a documented check-to-dispatch race.
- Agent Kubernetes reads and writes use hosted references and normal capability receipts. `KubernetesData`, `KubernetesControl` and `KubernetesExec` default Off, including migration. Pod terminal input is not authorized by ordinary host command grants. Preview grants no commit authority. Commit binds the exact session, preview, pod/resource identity and payload and consumes that preview once. Secret manifests are excluded from agent observations and structured mutation tools. Preview tokens expire after five minutes and belong to the issuing agent run and hosted session.

## Optional dependencies and limits

The core browser requires no kubectl or Helm installation. Helm features use a worker-local Helm binary, isolated temporary credentials and bounded output. Missing Helm is an actionable unavailable state. Current Pod and Node usage prefers the Metrics API. When it is unavailable, a bounded service discovery offers eligible in-cluster Prometheus endpoints; a unique complete result is selected automatically, while ambiguous providers require a choice. Current usage and Pod historical charts use the Kubernetes service proxy with fixed query templates; missing data stays unavailable or appears as gaps. Node-exporter data must have an explicit node label or a kube_node_info internal-IP join. No guessed instance-to-node mapping is used.

Exec currently requires the Kubernetes `v5.channel.k8s.io` WebSocket protocol. There is no SPDY compatibility fallback. Forwarding uses the SDK's Kubernetes WebSocket transport with bounded application relays. Live API success is not evidence that every proxy supports WebSocket upgrades.

Human administration supports the typed native workflows. Agent mutation tools currently expose reviewed apply, scale, rollout restart and delete of existing resources; node drain and Helm changes remain native UI operations. File browsing is read-only. Helm install, cloud cluster provisioning, Lens accounts/extensions and cross-cluster fleet search are outside this implementation.

## Evidence

The implementation uses fixture API servers and private worker protocols to test authentication, malformed payloads, stale identities, dry-run/commit separation, backpressure, route cancellation, terminal channels and forwarding. A Native AOT smoke executable exercises unknown-resource JSON, watch and YAML with the selected package. The native DesignQa tool has wide and split-pane Kubernetes scenes.

`docs/kubernetes-panel-testing.md` records live context checks, the full repository gate and remaining release acceptance. Direct host tests do not establish packaged Linux guest, SSH/VPN transport or every supported OS as release-tested.
