# autolaunch app

C# Utility for launching an app, if another app has been detected to be running

---

## features

- **single instance logger**
centralized logging to a file with timestamped messages. avoids duplicate log entries

- **process management**
shuts down an app, with fallback to force kill if necessary

- **ui logging window**
small form to display runtime logs in real time
supports command input

1. `stop` to stop the process watching loop
2. `start` to resume the process watching loop
3. `exit` to close the app

---

## installation

either

1. download and extract zip file in releases
or
2. bash```git clone https://github.com/s3ramis/crosshairx-autolaunch.git```
open solution in code editor and build

## usage

assuming the zip file has been downloaded and extracted:

1. replace mockup paths in programs.cfg
2. start `autolaunch-crosshairx.exe`
3. (optionally) put the exe in autostart
