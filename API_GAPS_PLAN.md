# API gap plan — idunno.Bluesky 7.0.1 features Traysky doesn't use yet

Work tracker for closing the gaps found in the idunno.Bluesky API audit. Companion to
[PLAN.md](PLAN.md) (architecture/milestones). Tick boxes as work lands; each chunk is sized to be
one commit (or a few) on `main` (see "work on main" — branch only for the risky Thread V2 chunk
if asked).

Source of truth for method signatures:
`~/.nuget/packages/idunno.bluesky/7.0.1/lib/net10.0/idunno.Bluesky.xml` (and `idunno.atproto`).
**Read the XML doc / decompile the signature before coding each chunk** — parameter shapes below
are from the audit's method names only.

## Verification gate (run at the end of EVERY chunk — do not tick the chunk until green)

This plan explicitly authorizes the build/test commands below (they override the CLAUDE.md
"don't run dotnet build/test/restore unless asked" rule *for this plan only*).

1. **Unit tests** — new pure logic goes in a `*Policy` class (no WinRT) and is linked into
   `Traysky.Tests/Traysky.Tests.csproj`, with tests in `Traysky.Tests/`:
   ```
   dotnet test Traysky.Tests/Traysky.Tests.csproj -p:Platform=ARM64
   ```
2. **ARM64 build of the app** — must succeed with 0 errors and no new warnings:
   ```
   dotnet build Traysky/Traysky.csproj -c Debug -p:Platform=ARM64
   ```
3. **Smoke check** — launch in preview mode (`preview.flag` in LocalState) or against a real
   account and exercise the chunk's "Done when" list. Record anything not verified in the
   chunk's *Notes* (be honest: "built + unit-tested, UI not exercised" is a valid status).
4. Update the chunk's checkbox, add a dated line to PLAN.md §0 only if it changes architecture.

Conventions for every chunk: pure decision logic in `*Policy`; network in a `*Service`; thin
ViewModel; optimistic UI with undo where the existing code does it; errors surfaced like the
existing pages (`Error` string, no throw to UI); log via `LogService`; strings in `Strings/`.

## Mapping from the audit list

| Audit # | Item | Chunk |
|---|---|---|
| 1 | `GetProfiles` batch hydration | 1 |
| 2 | `GetPostThreadV` (thread V2) | 6 |
| 3 | `UpdateAllRead` | 4 |
| 4 | Chat notification preferences | 4 |
| 5 | `ListConversationRequests`, `WithdrawJoinGroupRequest` | 4 |
| 6 | `DeleteQuote` | 3 |
| 7 | `GetStarterPack`/`GetStarterPacks` | 5 |
| 8 | `MuteActor` family, `GetListMutes`/`GetListBlocks` | 2 |
| new | Upload management / cancel | 7 |
| new | Live status | 8 |

Suggested order: foundation → low-risk wins → chat → discovery → riskiest → media → status.
Chunks are independent unless a *Depends on* line says otherwise.

---

## Chunk 0 — Baseline (do first, 15 min)

- [x] Run the full verification gate on a clean `main` and record the result below. If ARM64
      test/build is already red, fix or note it before starting so later failures are attributable.

Notes: 2026-10-17 — 296/296 tests pass; ARM64 build 0 errors. Pre-existing warnings (baseline,
not from this plan): 2× CS8602 in `Controls/ComposeBox.xaml.cs` (lines 171, 183; reported twice
each) and 2× MSTEST0017 in `OAuthCallbackPolicyTests.cs` (lines 21, 26). Smoke check not run.

---

## Chunk 1 — Batch profile hydration (`GetProfiles`)  *(audit #1)*

**Why:** the app calls `GetProfile` one actor at a time (notification/follow rows, mention
resolution, chat members). `GetProfiles` fetches up to 25 per request.

- [x] 1.1 `ProfileHydrationPolicy` (pure): split a list of actors into batches of ≤25, dedupe,
      preserve order, skip already-cached. Unit tests: 0/1/25/26/60 inputs, duplicates, mixed
      DID/handle.
- [x] 1.2 `ProfileCacheService` (or extend an existing cache — check `NotificationPollService`
      batch hydration first and reuse its pattern): `GetManyAsync(IEnumerable<AtIdentifier>)`
      using `agent.GetProfiles`, in-memory only (memory-trim friendly, hook `MemoryTrimService`).
- [x] 1.3 Switch call sites that loop over `GetProfile`/`GetProfileRecord` for multiple actors
      (grep `GetProfile(` in `ViewModels/` and `Services/`; leave single-profile page loads alone).
      **N/A — no such call sites exist** (see Notes).
- [x] 1.4 Verification gate.

Done when: opening a notification list / group member list issues ⌈n/25⌉ profile requests
instead of n (confirm via `LogService` request log or a counter in a debug build).

Notes: 2026-10-17 — The audit premise didn't hold: every `GetProfile` call is a single-profile
load (`ProfileViewModel`, `ProfileEditService`, session sign-in), and notification, follow, chat
member and group member rows already get their profile views embedded in the list responses
(`FeedMapper.ToActorItem` / `ChatMapper`). Nothing loops, so 1.3 has no call sites and the "Done
when" request-count check is vacuous. Landed the policy + service as groundwork instead;
`ProfileCacheService.GetManyAsync(IEnumerable<string>)` takes DIDs/handles and returns
`ProfileViewDetailed` keyed by normalized identifier. **First real consumer should be Chunk 5**
(starter-pack members/lists). Cache is capped (300), in-memory, cleared by `MemoryTrimService.Trim`.
The service itself is not unit-tested (needs the agent); the batching logic is (15 tests).
Built + unit-tested only; no UI to exercise. Tests 311 pass; build 0 errors, same 4 baseline warnings.

---

## Chunk 2 — Mute management: actor + list mutes  *(audit #8)*

**Existing:** `Mute`, `Unmute`, `MuteModList`, `UnmuteModList`, `GetMutes`, `GetBlocks`,
`BlockedMutedPage`. **Gap:** `MuteActor`/`UnmuteActor` (explicit actor overloads),
`MuteActorList`/`UnmuteActorList` (mute the *members* of a list — distinct from mod lists),
`GetListMutes`, `GetListBlocks` (the lists the user has muted/blocked).

- [x] 2.1 Extend `ListPolicy`/new `MuteTargetPolicy` (pure): which menu actions apply for
      (own list | mod list | curated list) × (muted | not muted). Tests for the matrix.
- [x] 2.2 `ModerationService`: add `MuteActorListAsync`, `UnmuteActorListAsync`,
      `GetMutedListsAsync`, `GetBlockedListsAsync` (paged). Decide `Mute` vs `MuteActor` by
      reading the XML doc — adopt `MuteActor` only if it is a strict superset.
- [x] 2.3 `BlockedMutedPage`: add "Muted lists" and "Blocked lists" sections/tabs
      (Segmented control already used elsewhere); row action to unmute/unblock the list.
- [x] 2.4 `ListPage` overflow menu: "Mute all members" / "Unmute members" for curated lists.
- [x] 2.5 Verification gate.

Done when: can mute a curated list from its page, see it under Blocked & muted → Muted lists,
and unmute it from there; mod-list mute/block still works.

Notes: 2026-10-17 — Built + unit-tested (319 pass, 0 build errors, same 4 baseline warnings); UI
and server behavior **not exercised**. Deviations from the plan:
- `Mute`/`MuteActor` (and `Unmute`/`UnmuteActor`): same signatures, `MuteActor` is not a superset —
  kept `Mute`, adopted nothing.
- The list calls live in `ListsService` (next to the existing mod-list mute/block), not
  `ModerationService`: `SetListMutedAsync(uri, muted, kind)` picks `MuteActorList`/`UnmuteActorList`
  for curation lists and `MuteModList`/`UnmuteModList` for moderation lists;
  `GetManagedListsAsync(blocks, cursor, limit)` wraps `GetListMutes`/`GetListBlocks`.
- 2.4: `ListPage` already had Mute/Block buttons for mod lists (no overflow entry), so curation
  lists reuse that panel: "Mute members"/"Unmute members" shown, Block hidden. `ListViewModel.CanSubscribe`
  split into `CanMute`/`CanBlock`; `ListPolicy.CanSubscribe` is now unused by the app (kept, tested).
- 2.3: tabs added to the existing `SelectorBar` (Blocked, Muted, Blocked lists, Muted lists); one
  `ListView` with a `ManagedRowTemplateSelector` switching person/list row templates.
- **Unverified risk:** `GetListMutes` is documented as returning *mod lists*. If the server only
  accepts `muteActorList` for mod lists, or doesn't return muted curation lists, a muted curation
  list may fail or won't appear under Muted lists (it would still be mutable/unmutable from its own
  page). Needs one manual check with a real account.

---

## Chunk 3 — Detach a quote (`DeleteQuote`)  *(audit #6)*

**Why:** Bluesky lets the quoted author detach their post from someone else's quote. Today
Traysky can only set gates on own posts.

- [x] 3.1 Extend `PostMenuPolicy` (pure): show "Detach my post from this quote" only when the
      post being viewed *quotes one of the user's posts* and is not already detached. Tests.
      (Needs the quoted-record `viewDetached`/`embeddingDisabled` state — check what
      `FeedMapper` already maps; add to the item model if missing.)
- [x] 3.2 `GatesService.DetachQuoteAsync` → `agent.DeleteQuote(...)`; also re-attach
      (`PostGate` update) if the library exposes it via `UpdatePostGate` — otherwise one-way and
      say so in the confirm dialog.
- [x] 3.3 Post menu item + confirm dialog + optimistic "detached" placeholder in the timeline row.
- [x] 3.4 Verification gate.

Done when: another account's quote of your post shows the menu item; after confirming it
collapses to the "removed by author" placeholder and the action disappears.

Notes: 2026-10-17 — Built + unit-tested (330 pass, 0 build errors, same baseline warnings); UI and
server behavior **not exercised**. Deviations from the plan:
- **The plan's API was wrong.** `agent.DeleteQuote` deletes the *user's own* quote record (it is just
  `DeletePost`), so it can't detach someone else's quote of your post. Detaching is done by adding the
  quoting post's URI to `detachedEmbeddingUris` on the *quoted* post's post gate. Implemented that via
  `GetPostGate` + `AddPostGate`/`UpdatePostGate` (existing rules preserved); `DeleteQuote` unused.
- Pure logic is in new `DetachQuotePolicy` (eligibility from the quoted URI's DID, order-preserving
  dedupe merge with the `Maximum.PostGateDetachedEmbeddingPosts` cap); `PostMenuState.ShowDetachQuote`.
- One-way, as the confirm dialog says: re-attach not offered. The existing "Reply and quote settings"
  already preserves the detached list when it rewrites the gate.
- The placeholder is applied after the write succeeds (not before), by making `PostItem.Embed`
  observable and swapping in an `Unavailable` embed. `FeedMapper` now maps `ViewDetached` to the same
  "Post unavailable" card, so detached quotes stay collapsed after a refresh.
- Only the menu on the *quoting* post card offers it; no entry from the notifications list.

---

## Chunk 4 — Chat: read-all, notification prefs, requests, withdraw  *(audit #3, #4, #5)*

**Existing:** `ListConversations`, `GetUnreadCounts`, `UpdateRead`, `AcceptConversation`,
group join-request approve/reject, `RequestJoinGroup`, etc.

- [x] 4.1 **Mark all read** — `UpdateAllRead`. Pure `ChatReadPolicy`: enabled iff any unread.
      `MessagesViewModel.MarkAllReadAsync` + toolbar/overflow button on `MessagesPage`; zero the
      unread badge optimistically and refresh `GetUnreadCounts` after. Tests for the policy.
- [x] 4.2 **Chat notification preferences** — `GetChatNotificationPreferences` /
      `SetChatNotificationPreferences` (use `Put…` only if `Set…` is a partial-update variant;
      confirm from XML). Extend `NotificationPreferencePolicy` (or a sibling
      `ChatNotificationPreferencePolicy`) with the option model and defaults; add a "Messages"
      section to `NotificationPreferencesPage` (+ mirror the `ToastService` toggle so muted chat
      prefs also suppress local toasts — check `NotificationAnnouncer`).
- [x] 4.3 **Message requests inbox** — `ListConversationRequests` (paged). `ChatAccessPolicy`
      gets "requests count" logic; `MessagesPage` gets a "Requests" segment with accept (existing
      `AcceptConversation`) / leave-and-block actions. Tests for ordering/empty-state policy.
- [x] 4.4 **Withdraw group join request** — `WithdrawJoinGroupRequest`. When the user has a
      pending request (from `RequestJoinGroup`/`GetJoinGroupLinkPreviews` state), show
      "Cancel request" in `GroupInfoPage`/join preview; extend `GroupChatPolicy` + tests.
- [x] 4.5 (both skipped, see Notes) Also wire the cheap siblings found in the audit, if the XML shows they're one-liners:
      `UnlockConversation` (counterpart of existing lock) and `ListMutualGroups` (profile page
      "Groups in common") — mark skipped if not.
- [x] 4.6 Verification gate.

Done when: Mark-all-read clears badges; chat prefs round-trip (change, relaunch, value
persisted server-side); a request can be accepted from the Requests tab; a pending group join
request can be withdrawn.

Notes: 2026-10-17 — Built + unit-tested (339 pass, 0 build errors, same 4 baseline warnings); UI and
server behavior **not exercised**. What landed and where it deviates from the plan:
- 4.1 `UpdateAllRead(ConversationStatus.Accepted)` via `MessagesViewModel.MarkAllReadCommand`; a button
  in the Messages header (enabled by `ChatReadPolicy.CanMarkAllRead`, main list only). Rows and badge
  clear at once, a refusal restores them, success re-probes `GetUnreadCounts`. Requests are not
  marked read (they don't count toward the badge). The result type of `UpdateAllRead` was only used
  for `Succeeded`/`StatusCode`.
- 4.2 `PutChatNotificationPreferences(Preferences)`, not `Set…` (the doc'd partial-update overload takes
  `Preference chat, Preference chatRequest`; I send both, so full replace is equivalent). The chat
  record has only *include (all/follows)* and *push*, no "in your list" switch, so the new "Direct
  messages" section on `NotificationPreferencesPage` has Push + "Only from people I follow" per
  Messages / Message requests. Section is hidden when the session has no DM access. **Toast mirror
  dropped:** Traysky has no local chat toasts (`NotificationAnnouncer` is posts-only), so nothing to
  suppress. Pure bits in `ChatNotificationPreferencePolicy`.
- 4.3 Requests already appeared inline in the Messages list with Accept/Decline; added an All /
  Requests `SelectorBar` where Requests loads through `ListConversationRequests` (paged). That
  endpoint returns `ConversationViewBase` rows (it can include `JoinRequestConversationView`); only
  plain `ConversationView` rows are shown, so the user's own pending group join requests do **not**
  appear there (doc says the live API returns everything as `ConversationView`, unverified). Accepting
  from the Requests tab removes the row. The Requests list never updates the Messages badge
  (`ChatReadPolicy.UpdatesBadge`). Policy: `ChatReadPolicy` (+ `ChatListMode`), not `ChatAccessPolicy`.
- 4.4 Withdraw is offered from the join-by-link flow: when the preview says a request is pending,
  the dialog now has "Cancel request" (`WithdrawJoinGroupRequest` with the preview's `ConversationId`,
  new optional field on `GroupJoinPreview`). There is no pending-request row in `GroupInfoPage`
  (the user isn't a member yet), so that part of the plan doesn't apply. Rule in
  `GroupChatPolicy.CanWithdrawJoinRequest`.
- 4.5 Skipped both: Traysky has no group *lock* UI, so `UnlockConversation` has no counterpart; and
  `ListMutualGroups` needs a new profile-page section, which isn't a one-liner.

---

## Chunk 5 — Starter packs: view & browse  *(audit #7)*

**Existing:** `GetActorStarterPacks`, `SearchStarterPacks`, `StarterPackPage` (may already
render a pack via another path — check before duplicating).

- [x] 5.1 `StarterPackPolicy` (pure): batch/ordering rules, "follow all" eligibility (skip
      already-followed/blocked/self), rate-limit chunking for bulk follow. Tests.
- [x] 5.2 `StarterPackService`: `GetAsync` (`GetStarterPack`) for deep links / pack page,
      `GetManyAsync` (`GetStarterPacks`, ≤25) for lists (profile tab, search results) to avoid
      N fetches; pair with Chunk 1 cache pattern.
- [x] 5.3 `StarterPackPage`: finish members list, "Follow all" with progress + cancel (reuse
      Chunk 7's cancel pattern if landed, else plain `CancellationToken`), list + feed previews.
- [x] 5.4 Deep link: `DeepLinkPolicy`/`BlueskyLinks` recognize `/starter-pack/{handle}/{rkey}` and
      `at://…/app.bsky.graph.starterpack/…` and route to the page. Tests.
- [ ] 5.5 Optional (defer unless trivial): `GetSuggestedStarterPacks` row on `ExplorePage`.
      **Skipped** — not trivial (new row/template on ExplorePage).
- [x] 5.6 Verification gate.

Done when: pasting a starter-pack URL opens the pack; "Follow all" skips people you already
follow and can be cancelled midway.

Notes: 2026-10-17 — Built + unit-tested (368 pass, 0 build errors, same baseline warnings); UI and
server behavior **not exercised**. What landed and where it deviates from the plan:
- The page, member list (via `GetList` on the pack's list) and Profile/Search entry points already
  existed; this chunk added the rest. Pure logic is in `StarterPackPolicy` (follow targets, chunking,
  lookup batching, status text, link parsing) and is linked into `Traysky.Tests`.
- "Follow all": `StarterPackViewModel.FollowAllCommand` (+ generated `FollowAllCancelCommand`). Fetches
  any unloaded member pages first, skips self / already-followed / blocked either way (from each
  member's `Viewer` state), follows in batches of 10 with a 400 ms pause, and stops on HTTP 429. Cancel
  is honoured between accounts (an in-flight follow completes and is counted). Ends with a one-line summary.
- `StarterPackService.GetAsync` backs the page when it is opened by AT URI (new `StarterPackArgs.AtUri`),
  and also supplies the pack's feed names, shown under the header. **`GetManyAsync` has no caller yet**
  (same situation as Chunk 1's `ProfileCacheService`): the profile/search rows already get packs in bulk
  from their list responses.
- Links: `StarterPackPolicy.TryParseLink` accepts `bsky.app/starter-pack/{actor}/{rkey}`, the older
  `/start/…` form, and `at://…/app.bsky.graph.starterpack/…`. Used when a link is clicked in a post
  (`RichTextBuilder`) and when one is submitted in the search box (`SearchPage`), plus new
  `traysky:starterpack/{actor}/{rkey}` / `?uri=at://…` deep links (`DeepLinkKind.StarterPack`).
  Manifest unchanged (scheme already registered).
- Not verified: `Viewer.Following`/`Blocking` actually being populated on `GetList` subjects (if the
  server omits them, "Follow all" would re-follow people you already follow and create duplicate follow
  records); `StarterPackView` is assumed to derive from `StarterPackViewBasic` (it compiles).

---

## Chunk 6 — Thread V2 (`GetPostThreadV`, `GetPostThreadOtherV`)  *(audit #2)*  ⚠ riskiest

**Why:** V2 returns a flat, depth-annotated list with server-side sort/branching controls and
a separate "other replies" (hidden/blocked) endpoint — better fit for the existing thread-gap
work than V1's nested tree. Today `PostPageViewModel` and `TimelineViewModel` use `GetPostThread`.

Suggest a branch (`thread-v2`) for this chunk if asked; otherwise land behind a setting.

- [x] 6.1 Read the V2 XML docs; write findings (sort options, branching factor, depth, how
      "has more replies" is signalled) into Notes below before designing.
- [x] 6.2 `ThreadFlattenPolicy` (pure): map V2 flat nodes (depth, parent link, `hasMore`/gap
      markers) → the existing thread row model; sort-mode enum (Top/Newest/Oldest/…). Heavy
      tests: deep chains, gaps, blocked/not-found nodes, root-above-anchor ancestors.
- [x] 6.3 `ThreadService` wrapper with V2 call + fallback to V1 on failure/unsupported.
      Setting "Use new thread view" (default on once stable).
- [x] 6.4 `PostPageViewModel`: sort picker, "Load more replies" using V2 gap pagination;
      `GetPostThreadOtherV` → "Hidden replies" expander.
- [x] 6.5 `TimelineViewModel` reply-context fetch (`GetPostThread` at ~line 266) — leave on V1
      unless V2 is strictly cheaper; document decision. **Left on V1** (see Notes).
- [x] 6.6 Verification gate; manually test a long thread and a thread with blocked replies.
      **Built + unit-tested only; manual thread tests not run** (see Notes).

Done when: a 200-reply thread opens fast, sort switching works, gaps load on demand, hidden
replies expander works, and flipping the setting off restores V1 behavior.

Notes: 2026-10-17 — Built + unit-tested (384 pass, 0 build errors, same baseline warnings); UI and
server behavior **not exercised**. Worked on `main` (no branch), behind a setting, so the risk is
contained by the V1 fallback.
- 6.1 findings (SDK 7.0.1, both calls are `[Experimental("BSKYUnspecced")]` → `NoWarn` added in
  `Traysky.csproj`):
  - `GetPostThreadV2(anchor, above=true, below=6 (0–20), branchingFactor=10 (0–100), sort="oldest"|"newest"|"top",
    subscribedLabelers)`. **Requires auth** (throws `AuthenticationRequiredException`). Replies *directly
    under the anchor* are all returned regardless of branching factor.
  - Result `PostThreadV2 { Thread: flat list of ThreadItem{Uri, Depth, Value}, ThreadGate, HasOtherReplies }`.
    Anchor is depth 0, parents negative, replies positive. `Value` is `ThreadItemPost`
    (`Post, MoreParents, MoreReplies, OpThread*, HiddenByThreadGate, MutedByViewer`), `…Blocked`,
    `…NotFound`, `…NoUnauthenticated`, or a bare `ThreadItemValue` for unknown types.
  - "Has more" is per post: `MoreReplies` (best-effort count) and `MoreParents`; thread-wide `HasOtherReplies`.
  - `GetPostThreadOtherV2(anchor, labelers)` → `IReadOnlyCollection<ThreadItem>` of top-level replies below the
    anchor (hidden by threadgate etc.); no ancestors, no anchor, no pagination.
  - There is **no cursor / gap pagination** in v2, so plan item 6.4's "Load more replies using V2 gap pagination"
    isn't possible. A post with `MoreReplies > 0` shows a "Show N more replies" link that opens that post as
    its own thread page instead.
- 6.2 `ThreadFlattenPolicy` (+ `ThreadSort`, `ThreadSortOptions`): ancestors = unbroken run nearest the anchor
  (a deleted/blocked ancestor cuts off everything above it and flags `AncestorsTruncated`); a blocked / not-found /
  unknown reply drops its whole subtree; author-hidden and viewer-muted replies are dropped with their subtree and
  counted (`HiddenCount`) unless `includeHidden`; display depth is capped at 6. `FeedMapper.FlattenThreadV2` /
  `FlattenOtherReplies` build the rows (reply-to handle tracked per depth).
- 6.3 `ThreadService.LoadAsync`: V2 when `SettingsService.UseThreadV2` (default **on**; Settings → Timeline →
  "New thread view"), V1 on any V2 failure or exception except 404 / an unavailable anchor. The page no longer calls
  `GetPostThread` itself.
- 6.4 Sort picker (Top / Oldest / Newest, default **Top** like bsky.app, persisted as `ThreadSort`) shown for v2
  threads that have replies; changing it refetches. "Show hidden replies" button under the thread when v2 reported
  `HasOtherReplies` or the policy hid some; it appends `GetPostThreadOtherV2` results (de-duplicated by URI).
- 6.5 `TimelineViewModel` stays on V1: it only needs a post's parent chain (`depth: 0`), V2 gives no saving, is
  unspecced, and would make the timeline depend on the experimental endpoint.
- Unverified: whether `top` sort and the 6-level / 10-branching defaults feel right on a 200-reply thread; that
  `GetPostThreadOtherV2` also returns the muted-by-viewer replies the main response drops (if not, they stay
  unreachable); what the server returns for `NoUnauthenticated`/threadgate-restricted anchors; depth numbering of
  `GetPostThreadOtherV2` items (handled either way: shallowest level shows at depth 1).

---

## Chunk 7 — Upload management & cancel  *(new request)*

**Existing:** `ComposeViewModel.UploadVideoAsync` — `agent.UploadVideo` (whole file in memory)
then a 60×2 s `GetJobStatus` poll. No cancellation, no progress, no quota awareness.
Image path uses `agent.UploadBlob`.

### 7A — Cancellation (client side, no new API) — do first
- [x] 7A.1 `UploadProgressPolicy` (pure): state machine
      `Idle → Uploading → Processing → Completed | Failed | Cancelled`, user-facing text for each,
      timeout/attempt accounting (replaces the magic 60×2 s), and "can cancel?" per state. Tests
      for all transitions incl. cancel-during-processing and double-cancel.
- [x] 7A.2 Thread a `CancellationToken` through `PostAsync` → `UploadVideoAsync` /
      image `UploadBlob` calls and `Task.Delay`/`GetJobStatus`; treat `OperationCanceledException`
      as `Cancelled`, not an error string. Make sure a cancelled post never creates the record
      and that already-uploaded image blobs are simply abandoned (orphan blobs are GC'd by the PDS).
- [x] 7A.3 Compose UI: **Cancel** button next to the upload status text (visible while
      Uploading/Processing), Post button re-enables, attachments are kept so the user can retry;
      per-attachment remove stays available once not uploading.
- [x] 7A.4 Cancel on navigation-away / popup close: decide (and document) whether an in-flight
      upload continues in the background or is cancelled; drafts (`DraftsService`) must not save
      a half-uploaded state.

### 7B — Server-aware chunked upload (real abort + progress) — investigate, then build
- [x] 7B.1 Read XML docs for `GetUploadLimits`, `StartUpload`, `UploadPart`, `FinishUpload`,
      `AbortUpload`, `GetUploadStatus`; note semantics, part size, and how they relate to
      `UploadVideo`. If they're not a drop-in for the video-service pipeline, record that here
      and stop 7B at 7B.2 (limits only).
- [x] 7B.2 **Preflight with `GetUploadLimits`**: show "N videos / M MB left today" and block
      attach when the daily quota is exhausted; extend `ComposeAttachmentPolicy` with limit
      checks (pure, tests).
- [x] 7B.3 If viable: `VideoUploadService` using Start/Part/Finish with per-part progress
      (`IProgress<double>` → determinate progress bar) and `AbortUpload` on cancel so the
      server drops the partial upload; `GetUploadStatus` for resume after a transient failure.
- [ ] 7B.4 (skipped, see Notes) `UploadCaptions` (and `UploadImage` if it adds server-side resizing beyond what
      `ImageCompressor` does): optional caption (.vtt) attach for videos — only if cheap.

- [x] 7.Z Verification gate — built + unit-tested only; manual cancel/upload runs NOT done (include: cancel at 0%, mid-upload, during processing; confirm no
      post is created and no stuck "Uploading…" text remains).

Done when: a user can cancel a video upload at any stage and compose returns to an editable,
retry-able state; (7B) quota shown and server upload aborted on cancel.

Notes: 2026-10-17 — **7A done; 7B not started** (so chunk 7 stays open). 7A built + unit-tested (397
pass, 0 build errors, baseline warnings); UI **not exercised** — cancel at 0% / mid-upload / during
processing still needs a manual run with a real video.
- `UploadProgressPolicy` (+ `UploadPhase`) holds the transitions (all idempotent; a late complete/fail
  after a cancel is ignored), `CanCancel`/`CanEditAttachments`, status text, and the poll budget
  (`PollInterval` 2 s × `MaxPollAttempts` 60, same as the old magic numbers).
- `ComposeViewModel.PostAsync` owns a `CancellationTokenSource`; the token goes to `UploadBlob`
  (named arg), `UploadVideo`, `Task.Delay` and `GetJobStatus`. `OperationCanceledException` → phase
  `Cancelled` (status "Upload cancelled", no `Error`), attachments kept. The post record is only
  created after all media uploads succeed, so a cancel never posts. The token is **not** passed to
  `agent.Post` itself — once media is up, cancel is no longer offered.
- UI: a "Cancel" `HyperlinkButton` beside the status text in `ComposeBox`, visible only while
  Uploading/Processing. `RemoveAttachment` is a no-op during an upload (the image loop iterates a
  snapshot). The "Upload cancelled" note clears when the attachments change.
- 7A.4 decision: the compose view model is a singleton, so closing the popup / navigating away does
  **not** cancel an in-flight upload; it finishes in the background and the post goes out. Drafts store
  only text + picked files, never upload state, so they can't capture a half-uploaded post.
- (7A, superseded by 7B.3 below) the single-request `UploadVideo` has no progress callback, and
  cancelling it only drops the client side.

2026-10-17 — **7B done** (except 7B.4). Built + unit-tested (411 pass, 0 build errors, baseline
warnings); **no real upload exercised**, so the multipart route is unproven against the live service.
- 7B.1 findings: `GetUploadLimits` → `UploadLimits { CanUpload, RemainingDailyVideos, RemainingDailyBytes }`
  (null remaining = no limit). `StartUpload(size, mime, name, …)` → `{ JobId, PartSize, PartCount, ExpiresAt }`;
  `UploadPart(jobId, part, bytes)` must match the server-derived length exactly and is idempotent;
  `FinishUpload(jobId)` is idempotent and returns the job to poll with `GetJobStatus` (validation failures
  show up there, not here); `GetUploadStatus` → `{ State, ReceivedParts, … }`; `AbortUpload` only works while
  the session is `Created` (a finishing session returns UploadNotReady) and releases the quota reservation.
  It *is* a drop-in for the video pipeline: the result feeds the same `GetJobStatus` poll.
- 7B.2 `ComposeAttachmentPolicy.ValidateVideoLimits` / `LimitsSummary` (pure, tested). Checked when a video
  is picked (`AddVideoAsync`); an exhausted allowance or a too-big file blocks the attach with a message,
  and "N videos, M MB left today" replaces the "Ctrl+Enter to post" hint while a video is attached. If the
  limits call itself fails the attach proceeds (the service is authoritative).
- 7B.3 `VideoUploadService.UploadAsync` (start → parts → finish) with `IProgress<double>` driving a
  determinate bar next to the status text, up to 3 rounds per upload where a failed part triggers
  `GetUploadStatus` and only missing parts are resent, and `AbortUpload` (10 s, best effort) on cancel or
  failure. Pure part-splitting/progress in `VideoUploadPolicy` (tested).
- **Fallback:** if `StartUpload` fails, the server's part layout doesn't fit the file, or the very first part
  is rejected, it aborts and uses the old single-request `UploadVideo` (indeterminate), so a service that
  doesn't speak multipart doesn't break video posting.
- **Unverified / risks:** part numbering is undocumented — assumed 1-based (`VideoUploadPolicy.FirstPartNumber`);
  if wrong, the first-part-rejected fallback hides it (video still posts, just without progress/abort) and
  the log shows `UploadPart 1 failed`. Whole video is sliced into per-part copies (≤ 100 MB cap, so
  memory is bounded). Cancel during a finishing session can't abort (server says not ready).
- 7B.4 skipped: captions need a .vtt picker, language choice and UI in the attachment card — not cheap, and
  `EmbeddedVideo` is currently built with `captions: null`.

---

## Chunk 8 — Live status  *(new request)*

**API:** `CreateLiveStatus`, `CreateStatus`, `UpdateStatus`, `DeleteStatus`, `GetStatus`
(`app.bsky.actor.status`; "live" status carries an external link/embed and an expiry).
Placement: **Profile** (display others' live state) + **Compose/Profile menu** (set your own),
because it is account state, not a feed item.

- [x] 8.1 Read XML docs; record: record shape, expiry semantics, allowed link domains, whether
      `ProfileViewDetailed.Status` already surfaces other users' status in lists/profiles.
- [x] 8.2 `LiveStatusPolicy` (pure): expiry → active/expired, remaining-time text, URL/domain
      validation, duration presets (30 m / 1 h / 2 h / 4 h), "is-live" badge decision. Tests incl.
      boundary at expiry, missing expiry, invalid URL.
- [x] 8.3 `LiveStatusService`: `GetOwnAsync`, `GoLiveAsync(url, duration)`, `ExtendAsync`
      (`UpdateStatus`), `EndAsync` (`DeleteStatus`). Cache own status; re-check on expiry timer
      (single timer, stop when none — respect idle-memory rules). **No `GetOwnAsync`/cache** (see Notes).
- [x] 8.4 UI (own): "Go live" entry in the profile overflow / account menu → small dialog (link
      + duration) → shows a "LIVE · 1 h 12 m left · End" strip on own profile.
- [x] 8.5 UI (others): LIVE badge on avatar + button to open the stream link in the default
      browser (reuse the external-link policy `ExternalMediaPolicy`/`LinkCardPolicy` so the same
      safe-link rules apply) on `ProfilePage` **only**; follow-list/search rows not done (see Notes).
- [ ] 8.6 Optional: tray tooltip/badge hint while *you* are live; Settings toggle to hide others'
      live badges. **Skipped** — optional, and a tray hint would need background work the tray
      icon displays (CLAUDE.md requires a documented reason).
- [x] 8.7 Verification gate.

Done when: go live with a Twitch/YouTube link → badge shows on your profile (and in the official
client), extend and end both work, expired status disappears without restart.

Notes: 2026-11-08 — Built + unit-tested (496 pass, 0 build errors; no warnings from the new files —
the 23 `CsWinRT1032` warnings a full rebuild prints are all in older files); UI and server behavior
**not exercised** (no real account or preview sample for a live status).
- 8.1 findings (SDK 7.0.1): `CreateLiveStatus(Uri uri, string title, string description, Blob? preview,
  int? durationMinutes)` writes the record; `GetStatus()` returns the own record (`Value.DurationMinutes`,
  `Value.CreatedAt`); `UpdateStatus(record)`; `DeleteStatus()` (no arguments, own status only). Other
  people's status arrives as `StatusView` on `ProfileViewDetailed.Status` (`Status` value, `Embed` as
  `EmbeddedExternalView`, `ExpiresAt`, `IsActive`, `IsDisabled`). Expiry is `CreatedAt + DurationMinutes`;
  the server's `IsActive` is only present when an expiry was set. The allowed-domain list is a server
  rule the docs don't state, so the dialog accepts any http(s) link with a dotted host and shows the
  server's message if it refuses.
- 8.2 `LiveStatusPolicy` (+ `LiveStatusInfo`), linked into `Traysky.Tests` (16 tests). A missing expiry or
  missing `IsActive` counts as live; expiry is exclusive. Links go through `BlueskyLinks.TryParseWebUri`
  (so `file:`/`javascript:` are dropped and nothing unsafe can be opened); `ExternalMediaPolicy` /
  `LinkCardPolicy` were not reused since they decide image/link-card questions, not safety.
- 8.3 `LiveStatusService` is static like `ProfileEditService`. **No own-status cache or global timer:** the
  profile page already loads the status with the profile, so `ProfileViewModel` holds it and the page runs
  one 30 s `DispatcherTimer` only while a live status is shown (stopped on unload or expiry). Extend reads
  `GetStatus`, then sets `DurationMinutes` so the end is old end (or now) + 30 m, counted from `CreatedAt`.
  `description` is sent empty and no preview image is attached.
- 8.4/8.5 `ProfilePage`: "Go live" button in the own-profile button row (hidden while live) → `LiveStatusDialogs`
  (link, optional title, 30 m/1 h/2 h/4 h) via new `IDialogService.AskGoLiveAsync`. A red LIVE strip under the
  handle shows title · time left, with **Watch** (anyone, opens the link in the default browser), and
  **+30 m** / **End** (own profile only). It is a strip, not a badge on the avatar.
- Not done: LIVE marks on follower/search/notification rows (`ActorItem` is built from `ProfileViewBasic`,
  which also carries `Status`, so it's a small mapper change plus a template edit per list — left out to keep
  this chunk to the profile). Nothing yet reads the status after a sign-in switch other than reloading the page.
- Unverified: that `CreateLiveStatus` with an empty description is accepted; that `UpdateStatus` keeps the
  embed intact; whether the official client shows the badge for a status created this way.

---

## Out of scope (revisit later)

Age assurance (`GetAgeAssuranceState`), generic record access (`*BlueskyRecord`), content
visibility/notification/conversation declarations, `SendMessageBatch`, `GetEmbedExternalView`,
`CreateStandardSiteEmbeddedCardGenerator`, reference-list opt-outs, `SearchPostsV`.

## Progress summary

| Chunk | Status | Date | Tests (ARM64) | Build (ARM64) |
|---|---|---|---|---|
| 0 Baseline | ☑ | 2026-10-17 | 296 pass | 0 errors, 4 pre-existing warnings |
| 1 GetProfiles | ☑ (no call sites to convert) | 2026-10-17 | 311 pass | 0 errors, 4 baseline warnings |
| 2 Mutes | ☑ (UI/server not exercised) | 2026-10-17 | 319 pass | 0 errors, 4 baseline warnings |
| 3 Detach quote (not `DeleteQuote`) | ☑ (UI/server not exercised) | 2026-10-17 | 330 pass | 0 errors, baseline warnings |
| 4 Chat | ☑ (UI/server not exercised; 4.5 skipped) | 2026-10-17 | 339 pass | 0 errors, 4 baseline warnings |
| 5 Starter packs | ☑ (UI/server not exercised; 5.5 skipped) | 2026-10-17 | 368 pass | 0 errors, baseline warnings |
| 6 Thread V2 | ☑ (UI/server not exercised; no v2 gap pagination exists) | 2026-10-17 | 384 pass | 0 errors, baseline warnings |
| 7 Uploads/cancel | ☑ (UI/server not exercised; 7B.4 skipped) | 2026-10-17 | 411 pass | 0 errors, baseline warnings |
| 8 Live status | ☑ (UI/server not exercised; profile page only, 8.6 skipped) | 2026-11-08 | 496 pass | 0 errors, no new warnings |
