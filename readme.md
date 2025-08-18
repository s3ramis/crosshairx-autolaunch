# autolaunch app

C# Utility for launching an app, if another app has been detected to be running

---

## features

- **process watching utility**
checks if apps specified by user are currently running

- **process management**
shuts down an app, with fallback to force kill if necessary

- **single instance logger**
centralized logging to a file with timestamped messages. avoids duplicate log entries

- **ui logging window**
small form to display runtime logs in real time
supports command input

1. `stop` to stop the process watching loop
2. `start` to resume the process watching loop
3. `exit` to close the app

---
## install
Download the latest release from the [releases page](https://github.com/s3ramis/crosshairx-autolaunch/releases).

1. go to the [releases](https://github.com/s3ramis/crosshairx-autolaunch/releases) section.
2. download the latest `autolaunch_crosshairx.zip`.
3. extract it anywhere on your system (excluding admin access folders).

## usage

assuming the zip file has been downloaded and extracted:

1. replace mockup paths in programs.cfg
2. start `autolaunch-crosshairx.exe`
3. (optionally) put the exe in autostart

the app specified under `open app:` in the `programs.cfg` file should now open automatically
if any app specified under  `watch apps:` in the `programs.cfg` is detected to be running

if you want to use the 'app to be opened' without having an app it depends on open, you can open
the log viewer via the system tray icon (double clicking or right click -> open log) and inputting
the `stop` command, to prevent the app closing.

if you wish to continue the app watching utility,
input `start` into the log viewer.

the app can be closed either by rightclicking the system tray
icon and choosing the respective button or inputtin the `exit` command into the log viewer
