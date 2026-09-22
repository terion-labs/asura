# ADR 0059: Host authentication for non-isolated Kubernetes workspaces

Status: Accepted

## Context

Non-isolated workspaces can route Kubernetes traffic through a private Tailscale,
VPN, or SSH connection service. That service runs in Linux and cannot use the
Mac's credential CLI or its login files. A kubeconfig with an approved `exec`
command therefore failed even though the same configuration worked on the host.

## Decision

Choose authentication placement from workspace isolation, independently of the
network backend. In a non-isolated workspace, read the configuration and resolve
credentials on the host. In an isolated workspace, retain configuration reading
and authentication inside the selected backend. There is no automatic fallback
from isolated authentication to host execution.

Reuse `KubernetesCredentialResolver`, including its exact-command fingerprint
check, executable lookup, output validation, timeout, and cancellation. This
supports standard ExecCredential tokens and client certificates without any
cloud-provider branches. Host authentication uses the host's network and login
files. Cluster API requests continue through the existing workspace route.

Send resolved connection material to the worker over its existing private framed
stdin/stdout channel. Do not send the host executable plan, host file paths, or
login directories. For renewal, the worker asks for credentials without supplying
a command or target. The host resolves the plan retained for that session. The
client still rejects a refresh that changes the API endpoint or TLS identity.

Use negative, sequential IDs for credential exchanges and positive IDs for panel
commands. A single worker reader dispatches credential replies separately from
commands, including while port-forward input is active. Writes are serialized on
both sides, buffered input is bounded, and serialized secret buffers are cleared.
No additional listening socket or persisted credential copy is introduced.

The connection editor explains authentication placement and its network behavior
next to command trust. An isolated workspace needs its own CLI and authentication
files; allowing explicit host authentication there is outside this change.

## Validation

Real worker-process tests use a credential command that refuses execution in the
worker environment. They cover host authentication, expiry and 401 renewal,
renewal failure redaction, route cancellation, review without execution, and trust
invalidation after command edits. A duplex channel test interleaves terminal input
with client-certificate renewal and checks failure when the host disconnects.
Existing terminal, watch, forwarding, and request-sequence tests remain in place.
