# UI-GITSETTINGS-STABILITY

## Reproduction
Application Settings can repaint/blank repeatedly and make the dynamically injected GitHub tab unreachable.

## Root cause
GitHubIntegrationUiBootstrap injected GitHubIntegrationControl into an already-visible SettingsForm from Application.Idle. The application also observes dynamic ControlAdded events and reapplies presentation/layout, so building and loading the large GitHub control could repeatedly mutate/re-layout the Settings dialog.

## Root fix
- Remove GitHubIntegrationControl injection from SettingsForm.
- Add Commands > Application > Git Settings.
- Open GitHub integration in a dedicated modal dialog with a stable control tree.
- Load GitHub integration only after that dedicated dialog is shown.
- Keep Application Settings independent of GitHub API/control loading.

## Regression contract
GitHubIntegrationUiBootstrap must not reference SettingsForm or add pages to its TabControl. SettingsForm must not instantiate GitHubIntegrationControl.
