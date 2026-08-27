# PGA RFU/CPU Temperature Display Throttle

## Scope

- Simplify PGA status bar temperature alternation in `DeviceInfoWidget`.
- Background queries run about once per second; swap timer is 2 seconds.
- When both RFU and CPU temperatures are available, refresh the label only on swap (or when availability changes), not on every background update.

## Success Criteria

- With both temperatures valid: UI updates on 2s swap timer only, plus once when a source becomes available/unavailable.
- With only one temperature valid: UI still updates when that source changes.
- Disconnect clears temperature display as before.

## Verification

- Static review of `deviceinfowidget.cpp` call paths.
