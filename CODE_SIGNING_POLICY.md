# Code signing policy

Releases of ScopeTrace are currently not code signed. This page describes how signing will work once it's set up.

## What gets signed

Only `ScopeTrace.exe` built by the GitHub Actions workflow in this repository (`.github/workflows/build.yml`) from a tagged commit. Binaries built anywhere else are never signed.

## Team and roles

| Role | Members |
| --- | --- |
| Committers and reviewers | [ELPoupa](https://github.com/ELPoupa) |
| Approvers (release signing) | [ELPoupa](https://github.com/ELPoupa) |

Multi-factor authentication on GitHub is required for every member.

## Privacy

ScopeTrace does not collect any data and does not connect to the internet. It only talks to the serial port you select. Captures and settings are stored locally in `%LOCALAPPDATA%\ScopeTrace`, and exports go to the folder you choose.

## Uninstalling

ScopeTrace doesn't install anything. Delete `ScopeTrace.exe`, and optionally the `%LOCALAPPDATA%\ScopeTrace` folder to remove saved captures and settings.
