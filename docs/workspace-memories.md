# Workspace memories

Workspace memories are on by default and stay in the local application profile. Recalled notes enter the selected model's context. No historical conversation backfill or background model calls run automatically.

Open **Memories** from the agent header to search, filter, edit, pin, archive, inspect revisions, export, or forget notes. Turning recall off preserves records for the user. Agent writes have a separate switch. Forgetting removes notes, revisions, and their search entries; previously exported files, backups, source conversations, and copies in external models are separate.

Saved definition identity owns memory, so renaming and reopening preserve it, while duplicated definitions start empty. Unsaved workspaces retain a memory identity in recovery snapshots. The store supports atomic transfer into a newly saved owner without merging another owner's notes. Quick terminal has no workspace memory. Portable workspace definitions do not include memory.

## Agent contract

`memory.brief`, `memory.search`, `memory.read`, `memory.save`, and `memory.archive` share one scoped service. MCP takes a live `workspace_id` from `asura.workspaces`; it works without a configured built-in model. Memory calls do not acquire or relax the terminal/browser operator lock. The normal action broker remains unchanged. Connected harnesses should call `memory.brief` when starting work and insert its returned data into their context; Asura cannot force an external harness to do so. Terminal agents need an MCP connection to access this store.

Every write requires the generation returned by a preceding memory read. Edits also require the current note revision. Forget, disable, and permission changes advance the generation; deleting a workspace permanently retires its scope in the same database transaction as definition removal. A conflicting edit returns the current version. Agents must reread and deliberately merge. Never retry with a guessed generation.

Create requests are idempotent per caller and workspace. Asura stores a digest of the request ID. Use a fresh UUID for each distinct operation. Native callers are identified by their host-stamped conversation run. Stateful MCP callers are identified by the server session. Stateless clients using the same bearer token share an authenticated principal and must use unique request IDs. Client-provided names never establish trusted authorship.

Search is plain text, with optional type, applicability, and offset. Search snippets are capped at 8,000 serialized UTF-8 bytes. Results carry `has_more`; advance offset by the number returned. Default recall omits archived, superseded, expired, and mismatched environment notes. Explicit reads expose status and dated source information. Handoffs expire from default recall after seven days. Similarity does not establish truth; contradictions between independently created notes still need review and explicit supersession.

Native writes have a host-stamped conversation source validated against the workspace. External and user evidence is labeled as reported. Deleted native sources become unavailable. Deleting a conversation offers to forget notes that cite it, including citations retained only in earlier revisions. Ordinary clearing preserves durable notes.

## Storage and context

SQLite transactions enforce memory permission, scope generation, note revision, idempotency, history, FTS updates, and a content-free mutation audit receipt together. This is a dedicated memory authorization boundary, independent of the exclusive operator broker. Agent tools cannot enable memory, pin notes, or permanently delete them. The editor, native tools, and MCP share known-secret checks, with literal-secret rejection again at persistence.

The quota is 50 MiB of logical note and revision content per workspace, with a warning at 80 percent. Titles allow 120 characters, bodies 2,000, and up to eight tags. Cleanup and export remain available. FTS5 uses secure deletion, and SQLite secure deletion is enabled on memory connections. This does not erase external backups or guarantee erasure from storage hardware.

Briefs are derived from records and bounded by UTF-8 bytes, with a smaller allocation for small-context models. Large notes do not prevent smaller notes from appearing. Pinned notes come first; omitted content remains available through explicit reads. Live built-in status is separate from historical notes; external activity is unavailable. At provider request boundaries, changed revisions invalidate older memory results and their original call arguments. Compaction excludes memory calls/results rather than promoting them into shared evidence. Already paraphrased assistant text may remain in a conversation; reset the conversation to discard that context.

Embeddings, graphs, Hindsight, automatic reflection, external activity heartbeats, and cross-device synchronization are deferred. Retrieval-quality benchmarking is a follow-up; the initial tests establish isolation, persistence, revisions, deletion, context handling, and transport parity.
