## Goal

- Select the main window titlebar logo at runtime from the current Settings language and current theme.
- Standard package should support English and Chinese titlebar logos.
- Russian package should support Russian and English titlebar logos.

## Local Hypothesis

- The selected resource variant is already fixed by src/app/CMakeLists.txt at build time: standard uses res_standard and Russian uses res_ru.
- The remaining missing piece is inside Controls::ThemeManager::getIconPath(AppLogo): it still uses a build-specific Chinese branch instead of reading the runtime language and choosing a language-specific logo only when that resource exists in the active qrc.

## Minimal Change

- Remove the temporary SGS_STANDARD_CN_BUILD compile definition and the build-specific AppLogo branch.
- Expose English logo aliases from res_ru/app.qrc.
- In ThemeManager, normalize the current Settings language via Utils::Translator when available, then:
  - choose Chinese logo aliases when language is cn and those aliases exist,
  - choose English logo aliases when language is en and those aliases exist,
  - otherwise fall back to the package-default logo/logo_light aliases.

## Validation

- Run file-scoped diagnostics on the touched files.