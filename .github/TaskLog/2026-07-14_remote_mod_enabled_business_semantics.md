# Remote MOD Enabled Business Semantics

## Scope

- Make the RemoteMiniBar MOD menu checkmark represent the effectively enabled
  business, not MainWindow's selected entry by itself.
- Replace the helper's separate `selectedBusinessId` and `enabled` snapshot state
  with one optional enabled-business identity.
- Keep Main as the only business/property/runtime owner and keep MOD editor
  requests revisioned and snapshot-authoritative.
- Do not change MainWindow/FancyTabWidget selection semantics or add another
  helper-side current/browsed state.

## Observations And Assumption

- Observation: Main currently publishes `selectedBusinessId` from
  `BusinessManager::selectedEntryBusiness()` and publishes the `Mod` property as a
  separate `enabled` flag.
- Observation: the helper combines both fields for the hosted editor, but the menu
  checkmark follows only `selectedBusinessId`; a Main-selected business can
  therefore remain checked while effective MOD is off.
- Observation: MOD menu clicks also overwrite the helper's selected ID
  optimistically before Main returns an authoritative snapshot.
- Observation: after the first enabled-business cleanup, clicking another menu item
  while one business is enabled sends `enabled=true` for the opened panel. In
  addition, ordinary editor actions always serialize the panel's displayed
  `enabled` value, so editing a disabled panel can overwrite the independently
  enabled business state in Main.
- Assumption: an "enabled business" means the selected provider while the
  authoritative `Mod` property is enabled. If either part is absent, RemoteMiniBar
  has no enabled business and shows no checked menu item.

## Design

1. The development protocol remains version 1 while publishing
   `mod.enabledBusinessId` and no longer publishing separate MOD-level
   `selectedBusinessId`/`enabled` snapshot fields. Main and helper are developed and
   deployed together, so this in-development schema correction does not require a
   compatibility version transition.
2. MOD requests identify their target with `businessId`. The `enabled` field is
   optional and its presence explicitly means that the user edited the Enabled
   switch; profile/editor actions omit it and cannot change business enablement.
3. Main derives `enabledBusinessId` only when `Mod` is enabled and the selected
   provider is present in the filtered remote business list.
4. Helper stores only `m_enabledModBusinessId`. Menu checkmarks, MOD button state,
   and hosted editor Enabled state are derived from that identity.
5. Clicking a menu entry only opens that business's editor. It never sends a MOD
   change request and never transfers enablement from the currently enabled
   business.

## Success Criteria

- A Main-selected business is not checked in the RemoteMiniBar menu while `Mod` is
  disabled.
- Exactly the effective enabled provider is checked while `Mod` is enabled.
- The MOD button and the active hosted editor use the same one-state derivation as
  the menu.
- Opening a disabled business while MOD is off does not mutate Main selection.
- Opening or editing any other MOD panel leaves the enabled business unchanged;
  only that panel's Enabled switch may change enablement.
- A fresh authoritative snapshot, not a local menu click, changes the checkmark.
- MainWindow/FancyTabWidget behavior and MOD request application remain otherwise
  unchanged.

## Verification Level

- `static`: confirm active CMake inclusion, all protocol key references, snapshot
  construction, request encode/decode, helper derivation, menu trigger behavior,
  and focused diff/whitespace checks.
- Do not build or run unless explicitly requested.

## Implementation Result

- Kept the private minibar IPC schema at development version 1. MOD requests now
  carry `businessId`; MOD snapshots now carry only `enabledBusinessId` in addition
  to the business list.
- Main publishes an enabled business only when the authoritative `Mod` property is
  true and the selected provider survives the remote-list filters.
- Helper now stores only `m_enabledModBusinessId`; menu checkmarks, MOD button state,
  and hosted editor Enabled state share that single derivation.
- Menu actions no longer overwrite the checked business locally or emit any MOD
  change request; they only open the selected editor panel.
- MOD request `enabled` is now optional. `RemoteModEditorHost` includes it only for
  an Enabled-switch edit, while ordinary profile/editor actions omit it. Main only
  updates business selection, the `Mod` property, and Tx selected business when the
  field is present.
- Updated `minibar_cs_helper_scpi_architecture.md` to document the development
  version 1 schema and enabled-business semantics.

## Static Verification Performed

- Confirmed the modified protocol, service, and helper source units remain included
  by their active CMake targets.
- Confirmed request encode/decode use `businessId`, snapshot production/consumption
  use `enabledBusinessId`, and both sides continue to use the shared development
  protocol version 1 constant.
- Confirmed no active RemoteMiniBar source retains `selectedBusinessId`,
  `m_selectedModBusinessId`, or `m_modEnabled`.
- Confirmed the menu trigger restores authoritative checking before opening a panel
  and never emits a MOD request.
- Confirmed Enabled-switch requests encode `enabled`, ordinary editor actions omit
  it, decode preserves that distinction, and Main guards every selection/Mod/Tx
  enablement write behind the explicit enabled intent.
- Focused `git diff --check` passed. Git reported only the repository's existing
  LF-to-CRLF working-copy warnings.
- No build or runtime verification was performed.
