<h1 align="center">Jellyfin TuneIn Plugin</h1>
<h3 align="center">Part of the <a href="https://jellyfin.media">Jellyfin Project</a></h3>

<p align="center">
Jellyfin TuneIn Plugin is a plugin built with .NET
</p>

## Build Process
1. Clone or download this repository
2. Ensure that you have .NET Core SDK installed and configured correctly
3. Build the plugin with the following command:
```sh
dotnet publish --configuration Release --output bin
```
4. Place the resulting .dll file, along with HtmlAgilityPack.dll, in a folder called `plugins/` under the program data directory or inside the portable install directory