# RFExtra

Here includes a collection of Ravenfield plugins for several features.

## Constitude
- [RavenM.LocalModLoader](RavenM.LocalModLoader/README.md)

  A plugin to enable RavenM([CHN Edition](https://github.com/RavenfieldCommunity/RavenM/releases/tag/test)) to load local mods.
  
# Build 
- Install [.Net SDK 9 or newer](https://dotnet.microsoft.com/) and [.Net Framework 4.6 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net462)

- Add nuget source on Terminal
  
  If you wish to use Github package, refer [docs.github.com](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-to-github-packages) first.
  
  ```sh
  # Optional 1, BepInEx BaGet
  dotnet nuget add source https://nuget.bepinex.dev/v3/index.json --name bepinex
  # Optional 2, Github package, replace with your username and token
  dotnet nuget add source https://nuget.pkg.github.com/RavenfieldCommunity/index.json --name github username GITHUB_USERNAME --password GITHUB_PERSONAL_ACCESS_TOKEN_CLASSIC --store-password-in-clear-text 
  ```
  
- Build src as normal dotnet projects by `dotnet build` or `dotnet publish`