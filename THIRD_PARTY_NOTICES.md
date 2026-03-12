# Third-Party Notices

This project bundles third-party tunnel executables for runtime use on Windows.

## Bundled Tools

### 1. SlipNet

Upstream repository:

`https://github.com/anonvector/SlipNet`

Bundled file:

- `tools\slipnet-windows-amd64.exe`

Purpose:

- Used by this project to launch `DNSTT`, `DNSTT + SSH`, `NoizDNS`, and `NoizDNS + SSH` style profiles on Windows.

### 2. slipstream-rust-deploy

Upstream repository:

`https://github.com/mirzaaghazadeh/slipstream-rust-deploy`

Bundled file:

- `tools\slipstream-client.exe`

Purpose:

- Used by this project to launch `Slipstream` style profiles on Windows.

## Related Reference Project

This repository was also informed by the Windows GUI project below when evaluating Windows-side packaging and runtime behavior:

`https://github.com/mirzaaghazadeh/SlipStreamGUI`

## Notes

- These tools are not authored in this repository.
- Their source code, releases, maintenance, and licensing are controlled by their upstream maintainers.
- If you redistribute this repository, review the upstream repositories and their licenses before publishing binaries.
