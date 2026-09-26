# General agent file attachments

Research and implementation: 2026-09-27. Issue: asura-n0gh.

## What other tools do

| Tool | Attachment handling | How the model accesses it |
| --- | --- | --- |
| Codex desktop, installed build 26.915.31945 | Local file references plus managed copies for imported/pasted content; copies or uploads local files when the agent host is remote. | File paths for tools, with separate image inputs. The installed attachment manager stores its managed-file registry under the Codex attachments directory. No general native model-file-format promise follows from accepting a file. |
| T3 Code, c9a0e8a119271765196dec38ef91395713da4213 | Generic file and image descriptors, stored attachment bytes; up to eight attachments, 50 MiB for a generic file and 10 MiB for an image. | All attachments receive an on-disk path in the prompt. Each adapter selects native inputs. Codex and Claude adapters pass images natively; generic files remain accessible through tools. Folded pasted text is deliberately path-only. |
| Xum, 96ec89ac7d468bf2be04fa32d4985f7e7cf25953 | Images/PDFs have a provider input path. Other files are staged into the selected workspace runtime, up to 10 MiB each. | Generic files live under `.xum/user-attachments`, outside Git tracking; tools read them. Native provider formats are separate from the generic staging path. |
| OpenCode V2, current attachment documentation | UTF-8 text, directories and supported raster images have documented prompt representations; picker selection is limited to 20 MiB. | Text is decoded, directories listed and supported images passed as media. The attachment documentation explicitly warns that arbitrary binary acceptance does not imply inclusion in a model request. |

Primary evidence:

- Codex desktop: local `/Applications/ChatGPT.app/Contents/Resources/app.asar`, `.vite/build/main-DUHZj4_w.js`, attachment manager methods `uploadLocalFiles`, `createManagedFileAttachment`, `createLocalFile`, and persistent registry handling. Behavior inspected only; no application code copied. This is evidence about the installed desktop build, not the CLI or a universal provider API.
- [T3 attachment contracts](https://github.com/pingdotgg/t3code/blob/c9a0e8a119271765196dec38ef91395713da4213/packages/contracts/src/orchestration.ts), [provider preparation](https://github.com/pingdotgg/t3code/blob/c9a0e8a119271765196dec38ef91395713da4213/apps/server/src/provider/Layers/ProviderService.ts), and [Claude adapter](https://github.com/pingdotgg/t3code/blob/c9a0e8a119271765196dec38ef91395713da4213/apps/server/src/provider/Layers/ClaudeAdapter.ts).
- [Xum staging](https://github.com/coder/xum/blob/96ec89ac7d468bf2be04fa32d4985f7e7cf25953/src/node/utils/attachments/stageWorkspaceAttachment.ts), [format classification](https://github.com/coder/xum/blob/96ec89ac7d468bf2be04fa32d4985f7e7cf25953/src/common/utils/attachments/supportedAttachmentMediaTypes.ts), and [composer import](https://github.com/coder/xum/blob/96ec89ac7d468bf2be04fa32d4985f7e7cf25953/src/browser/features/ChatInput/useComposerAttachments.ts). No Xum implementation code copied.
- [OpenCode V2 attachments](https://opencode.ai/v2/docs/attachments).
- [OpenAI file inputs](https://developers.openai.com/api/docs/guides/file-inputs): native formats depend on the endpoint. Responses supports documents/text/spreadsheets and PDFs; Chat Completions file parts support PDFs. PDF processing can include page images, while non-PDF documents generally contribute text. This describes the API, not proof that a desktop client uses those input types.

## Asura implementation

The picker accepts any regular file. File pasting uses the same importer. Supported raster images continue through the existing vision-input path when the selected provider supports images. Every other file, including a binary or an image for a text-only provider, is imported as a general attachment.

General attachments are immutable byte snapshots in the encrypted profile database. The transcript stores only ID, original filename and size. This avoids both mutable source paths and expanding a chat checkpoint by the attachment's size. A sent attachment remains available after the source is removed, a conversation is reopened, or a run is forked in the workspace.

The provider receives a reference and two tools:

- `attachments.list` pages through files in the durable transcript, including compacted turns.
- `attachments.open` checks that the ID belongs to the current conversation, checks the stored snapshot's scope and checksum, provides a paged UTF-8/UTF-16 text preview when possible, and creates a working copy in the workspace's local terminal environment on the initial call. Further text pages use byte offsets and do not create additional copies. For isolated workspaces, the bytes travel on stdin through the registered isolated command runtime. A closed/unregistered workspace cannot fall back to host staging.

PDFs, Office files, archives, audio/video and unknown formats are preserved byte-for-byte. The agent can process them with tools installed in that workspace. This change does not add native PDF/audio/video request parts, install decoders, or claim every model can interpret every format. Opening an attachment never executes it or extracts an archive automatically. The working-copy path is local to the workspace, not an arbitrary SSH target.

Limits: eight attachments per prompt, 50 MiB per general file, 200 MiB for general files in one prompt, and 1 GiB of stored file bytes per conversation scope. Existing direct image limits remain four images and 8 MiB total. Errors retain the draft and identify import/storage failures. Committed file snapshots are reclaimed after deletion/retention removes their last checkpoint reference; forks retain shared snapshots. Unsent abandoned imports remain bounded by the scope storage limit and need follow-up cleanup (asura-uudi).

## Simplification decisions

S1, chosen: `AgentAttachmentImport.AddFilesAsync` and `attachments.open` use one arbitrary-file path. This removes extension-specific text/code attachment plumbing and avoids eager text injection into every provider request.

S2, chosen: `DesktopAgentAttachmentService` uses the existing workspace command boundary for transfer. It does not introduce a separate sandbox, attachment server, host directory mount or model-specific parser framework.

## Verification

Tests cover picker/clipboard import, file-only sends, draft removal, source deletion, arbitrary bytes, Unicode preview paging, checkpoint restore, database reopen, cross-workspace and cross-conversation denial, shared fork retention, and duplex transfer through a workspace runtime. The duplex test runs the actual transfer process with a controlled runtime plan; it does not claim to test a live VM or a live model API.
