# SlipNet Windows Portable Launcher

Built with vibe coding using Codex.

`SlipNet Portable Launcher` is a portable Windows 10/11 desktop launcher for SlipNet-style profiles.

It imports `slipnet://` configs, stores profiles locally next to the app, starts the correct tunnel engine, and exposes the tunnel through SOCKS and an optional local HTTP proxy. The Windows implementation is intentionally different from the original Android app, but it aims to provide the same practical result for supported profile types.

## What It Does

- Imports plain `slipnet://` profile URIs.
- Creates and edits local profiles in a portable way.
- Starts supported tunnels from Windows using bundled helper executables.
- Exposes the tunnel as a local SOCKS endpoint.
- Optionally exposes a local HTTP proxy that forwards into the SOCKS tunnel.
- Optionally sets the Windows proxy while the tunnel is active.
- Stops the tunnel when the app stops it, and warns before closing the app while a tunnel is running.

## Supported Profile Types

Launchable with the current Windows runtime:

- `Slipstream`
- `DNSTT`
- `DNSTT + SSH`
- `NoizDNS`
- `NoizDNS + SSH`

Accepted by the config parser but not fully implemented as distinct Windows runtime modes:

- `Slipstream + SSH`
- `SSH`
- `DoH`
- `Snowflake`
- `Naive`
- `Naive + SSH`

## How It Works

### 1. Import

The app accepts `slipnet://...` profile URIs and decodes the base64 payload into local profile data.

Supported import behavior:

- Profile versions `v1` through `v17`
- Hidden resolvers in `v17`
- Slipstream and DNS-style profiles

Not supported:

- `slipnet-enc://` encrypted or locked import flows

### 2. Local Storage

The launcher is portable. It stores its state under a local `data\` folder beside the executable:

- `data\profiles.json`
- `data\settings.json`

This means the app can be moved together with its data and bundled tools.

### 3. Tunnel Runtime

The launcher does not reimplement the tunnel engines itself. Instead, it starts the appropriate bundled executable:

- `tools\slipnet-windows-amd64.exe` for `DNSTT` and `NoizDNS` based profiles
- `tools\slipstream-client.exe` for `Slipstream` based profiles

When the tunnel starts successfully, the launcher waits for the local SOCKS port from the selected profile to become reachable.

### 4. Optional HTTP Proxy

If enabled, the app starts a local HTTP proxy and forwards HTTP/HTTPS traffic into the SOCKS tunnel.

This makes the tunnel easier to use with browsers and Windows applications that expect an HTTP proxy instead of a SOCKS endpoint.

### 5. Optional Windows Proxy

If enabled, the launcher temporarily updates the Windows proxy settings while the tunnel is active, then restores the previous settings when the tunnel stops.

## Why This Is Portable

The project is designed so the runtime dependencies can live inside the repo and inside published builds:

- profiles and settings live in `data\`
- helper binaries live in `tools\`
- publish output includes the `tools\` directory automatically

No installer is required for normal use.

## Requirements

For users:

- Windows 10 or Windows 11
- x64 system

For development:

- .NET 8 SDK
- Windows desktop build environment for `net8.0-windows`

## Running the App

### Option 1. Run from source

```powershell
dotnet run --project .\SlipNetPortableLauncher.csproj
```

### Option 2. Build

```powershell
dotnet build .\SlipNetPortableLauncher.csproj -c Release
```

### Option 3. Publish a portable executable

```powershell
dotnet publish .\SlipNetPortableLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false
```

Published output will be placed under:

`buildbin\Release\net8.0-windows\win-x64\publish\`

## Using the App

1. Start the launcher.
2. Import one or more `slipnet://` URIs.
3. Select the profile you want.
4. Review the profile fields.
5. Click `Start Tunnel`.
6. Use the SOCKS endpoint from the profile, or enable the local HTTP proxy / Windows proxy options.
7. Click `Stop` when done.

If the user closes the window while a tunnel is running, the app asks for confirmation and stops the tunnel before exiting.

## Bundled Tool Repositories

This project uses these upstream repositories for the bundled Windows tunnel executables:

1. SlipNet

Repository:

`https://github.com/anonvector/SlipNet`

Used for:

- `tools\slipnet-windows-amd64.exe`

Role in this project:

- Runs `DNSTT` and `NoizDNS` family profiles on Windows

2. slipstream-rust-deploy

Repository:

`https://github.com/mirzaaghazadeh/slipstream-rust-deploy`

Used for:

- `tools\slipstream-client.exe`

Role in this project:

- Runs `Slipstream` family profiles on Windows

Additional related Windows GUI reference:

`https://github.com/mirzaaghazadeh/SlipStreamGUI`

See [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md) for a compact third-party summary.

## Project Layout

```text
SlipNetPortableLauncher/
|-- Models/
|-- Services/
|-- tools/
|-- MainForm.cs
|-- ImportProfilesForm.cs
|-- Program.cs
|-- SlipNetPortableLauncher.csproj
|-- Directory.Build.props
|-- README.md
|-- THIRD_PARTY_NOTICES.md
```

## Important Limitations

- This is not a kernel-level full-device VPN implementation.
- The current Windows approach is based on tunnel helper processes plus SOCKS/HTTP proxy bridging.
- `slipnet-enc://` encrypted profiles are not supported.
- `Slipstream + SSH` profiles are accepted by the parser, but the launcher does not currently add a separate Windows SSH layer on top of the Slipstream client.
- Parsed tunnel types outside the implemented set are not launchable yet.
- Actual connection success depends on the validity of the imported profile and the remote infrastructure.

## Repository Notes

- Build output is redirected to `buildbin\` and `buildobj\` by `Directory.Build.props`.
- Generated files and local state are ignored by `.gitignore`.
- The repo intentionally keeps the helper executables under `tools\` so published builds work without manual EXE selection.

## Development Notes

Useful commands:

```powershell
dotnet build .\SlipNetPortableLauncher.csproj -c Release
dotnet publish .\SlipNetPortableLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false
```

## Disclaimer

This repository is a Windows launcher around upstream tunnel tools. The bundled executables are third-party components maintained in their own upstream repositories.
