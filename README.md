# Commons Diversified Uploader (Windows)

A native Windows app for sports photographers who contribute to
[Wikimedia Commons](https://commons.wikimedia.org/). It reads jersey numbers
and jersey colours from match photos, maps them to players through the team
rosters on Wikipedia/Wikidata, proposes the matching Commons categories and
uploads the reviewed photos.

This is a community project. It is not affiliated with or endorsed by the
Wikimedia Foundation. The macOS and Linux counterparts live in
`commons-diversified-uploader-macos` and `commons-diversified-uploader-linux`;
all three share the same project file format (`commons-yukleme.json`, stored
next to the photos).

## Layout

- `core/` – submodule with the shared library (`commons-diversified-uploader-core`):
  analysis (Florence-2 on ONNX Runtime), roster lookup, matching, Commons
  client and its tests. Also used by the Linux app.
- `src/Uploader` – the WinUI 3 app (Windows App SDK). The interface is built
  in C#; `Strings.cs` holds the English texts (the source language is Turkish).
- `packaging/screenshots.ps1` – captures every screen of an unpackaged build;
  CI uses it to check the interface.

## Building

Requirements: Windows 10 2004 or later, .NET 10 SDK.

```sh
git clone --recurse-submodules https://github.com/HakanIST/commons-diversified-uploader-windows.git
dotnet test core
# run without packaging
dotnet run --project src/Uploader -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None
# MSIX package for the Microsoft Store (unsigned; the Store signs it)
dotnet publish src/Uploader -c Release -r win-x64 -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
```

The recognition model (about 1 GB) is downloaded from Hugging Face on first
use. Login opens Wikimedia's OAuth 2.0 page in the default browser, which
returns to the app through the `tr.tall.commonsdiversifieduploader` URI
scheme; this return only works in the packaged (MSIX) build, the unpackaged
build falls back to pasting the address. Credentials are encrypted with
DPAPI for the current Windows user.

On a machine without Windows the C# code can be type-checked with
`dotnet build src/Uploader -p:TypeCheckOnly=true -p:Platform=x64 -r win-x64`
(the build stops at the Windows-only resource step after compiling).

## Privacy

Photos are analysed on the device. The telemetry of the bundled ONNX Runtime
is disabled. See the
[privacy policy](https://github.com/HakanIST/commons-diversified-uploader-macos/blob/main/PRIVACY.md).

## Contributing

Commit messages follow the
[Wikimedia commit message guidelines](https://www.mediawiki.org/wiki/Gerrit/Commit_message_guidelines).

## License

[Apache License 2.0](LICENSE)
