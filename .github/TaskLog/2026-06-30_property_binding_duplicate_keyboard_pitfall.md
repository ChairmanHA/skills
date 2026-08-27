# Property Binding Duplicate Keyboard Pitfall Documentation

## Scope

- Add a KnowledgeBase pitfall document for the case where one shared `PropertySystem::IProperty` is bound to multiple visible or hidden controls/panels, causing multiple `beginEditing` subscribers to open duplicate soft keyboards.
- Update `.github/KnowledgeBase/Index.md` so future UI/property work can discover the pitfall.

## Context

- The issue was observed during the old Analog / HTRA same-name provider model: inactive hidden panels still subscribed to shared property signals, so one user click could open more than one `TouchNumKeyboard`.
- The current code has removed the old double-provider path for HTRA basic modulation, but the general pitfall still applies anywhere a global property is shared by multiple widgets.

## Success Criteria

- A new `Pitfalls/` document explains symptoms, root cause, prevention rules, and when `PropertyBindingHelper::isEditTriggerFromWidget(...)` is appropriate.
- `Index.md` links the new document in the Pitfalls section.

## Verification

- Static documentation review.
- Targeted search confirms the new document is indexed.
