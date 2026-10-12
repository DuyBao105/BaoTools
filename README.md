# BaoTools
<p>
  <img align="right" height="250" alt="hachimi" src="https://github.com/user-attachments/assets/33aa8bb5-df11-4b33-b5d6-9a8350495c8b" />

  [Discord](https://discord.gg/baocobap) • [Website](https://baotools.baotranduy666666.workers.dev/) • [Git Mirror](https://github.com/DuyBao105/BaoTools)
  
  A Windows desktop client for managing Steam manifest/lua configurations, built with WPF on .NET 8.
    
  BaoTools browses and installs manifest sources, edits stplug-in lua files (depot pinning,
  per-depot enable/disable), manages compatibility modes, and injects a companion plugin into Steam's
  store pages.
  
  It ships fully translated in 29 languages and auto-updates via Velopack.
  <br><sub>Found a translation error? Tell us about it over on [Discord](https://discord.gg/baotools)</sub>
</p>

## Statistics & Download
<div>
  <a href="https://github.com/DuyBao105/BaoTools/releases/latest/download/BaoTools_Setup.exe">
    <img src="https://img.shields.io/github/downloads/DuyBao105/BaoTools/BaoTools_Setup.exe?displayAssetName=true&style=for-the-badge" />
  </a>
  <a href="https://github.com/DuyBao105/BaoTools/releases/latest/download/BaoTools.exe">
    <img src="https://img.shields.io/github/downloads/DuyBao105/BaoTools/BaoTools.exe?displayAssetName=true&style=for-the-badge" />
  </a>
</div>

<a href="https://www.star-history.com/?repos=DuyBao105/BaoTools&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=DuyBao105/BaoTools&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=DuyBao105/BaoTools&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=DuyBao105/BaoTools&type=date&legend=top-left" />
 </picture>
</a>

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the released installer bundles a
  check for the .NET 8 **Desktop Runtime** and installs it if missing; [building from source](https://github.com/DuyBao105/BaoTools/blob/main/CONTRIBUTING.md#building-from-source--developing) needs
  the full SDK

## Installation
You can find release builds on the [baotools website](https://DuyBao105.github.io/BaoTools) or in the [releases](https://github.com/DuyBao105/BaoTools/releases/latest) tab. 

## Credits / Adjacent software

- [Millennium](https://steambrew.app/): the Steam plugin framework whose injection API this app
  polyfills when Millennium isn't installed
- [Velopack](https://velopack.io/): installer and auto-update framework

## Licence

MIT. See [LICENSE](LICENSE).
