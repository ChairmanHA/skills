# Digital Spectrum Plot Helper Plan

## Goal
- Extract reusable QCustomPlot spectrum styling and a lightweight spectrum chrome container from DigitalSpectrumDialog.
- Apply the new container to the Spectrum tab first.
- Reuse existing x-axis text tick generation and Start/Stop footer text updates.

## Local Hypothesis
- The current Spectrum tab layout is controlled entirely in digitalspectrumdialog.cpp, so introducing a QWidget helper plus reusable plot-style function is a local refactor that should not affect data flow from DigitalPanel.
- A narrow Debug compile of the AnalogModulation target should catch any integration issues.

## Planned Changes
- Add a small reusable helper module under src/plugins/analog for QCustomPlot styling and a spectrum plot pane widget.
- Replace the raw Spectrum tab title/plot layout in DigitalSpectrumDialog with the new pane.
- Keep existing spectrum data update logic, moving only presentation responsibilities into the helper.

## Validation
- Run a minimal Debug build for AnalogModulation-related compile path via existing CMake Debug build.
