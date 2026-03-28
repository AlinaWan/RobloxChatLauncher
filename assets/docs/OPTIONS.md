# Option Documentation

## Client Options
| Option            | Type     | Summary                                                                                                                                                                                                                                               | Param |
| ----------------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----- |
| --force-run       | `switch` | Launches without starting a Roblox client and immediately attaches to any currently running Roblox instance. Bypasses the recent process and log file checks.                                                                                         | -     |
| --launch-homepage | `switch` | Launches the Roblox client without a URI to open the Roblox homepage instead of a game. This option is automatically added when Roblox Chat Launcher is run from the Start menu or desktop shortcut.                                                  | -     |
| --allow-multiple  | `switch` | Bypasses the single instance check and does not assign a mutex to the process.                                                                                                                                                                        | -     |
| --uninstall       | `switch` | Silently restores the URI protocol handler registry key to point to a resolved Roblox client and immediately exits the program. The client is automatically run with this option when uninstalling the program via the "Add or remove programs" menu. | -     |

## Installer Options
| Option        | Type     | Summary                                                                                                                                    | Param |
| ------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------ | ----- |
| /CLEANINSTALL | `switch` | Uninstalls the previously installed version of Roblox Chat Launcher by silently running the uninstaller before installing the new version. | -     |
| /FORCERUN     | `switch` | Immediately runs the Roblox Chat Launcher client with the --force-run switch after installation.                                           | -     |
| /CLEARAPPDATA | `switch` | Forcefully removes the Roblox Chat Launcher local app data folder before installation.                                                     | -     |
| /FORCEPURGE   | `switch` | Forcefully purges the target installation directory before installation without using the uninstaller. **This is a dangerous option.**     | -     |
