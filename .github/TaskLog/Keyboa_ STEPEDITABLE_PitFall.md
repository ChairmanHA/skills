# Wayland Center Level StepEditable Disable
- `src/libs/business/utils.cpp` enables the keyboard title `EditableWidget` by
  checking `metadata()->hasProperty(PropertyMetadata::STEPEDITABLE)`, so writing
  `STEPEDITABLE = false` would still enable the editor.
