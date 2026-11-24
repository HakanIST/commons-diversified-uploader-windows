# Commons Diversified Uploader (Windows)

A native Windows app for sports photographers who contribute to
[Wikimedia Commons](https://commons.wikimedia.org/). It reads jersey numbers
and jersey colours from match photos, maps them to players through the team
rosters on Wikipedia/Wikidata, proposes the matching Commons categories and
uploads the reviewed photos.

This is a community project. It is not affiliated with or endorsed by the
Wikimedia Foundation. The macOS counterpart lives in
`commons-diversified-uploader-macos`; both apps share the same project file
format (`commons-yukleme.json`, stored next to the photos).

## Status

- `src/UploaderCore` – analysis (Florence-2 on ONNX Runtime), roster lookup,
  matching and Commons client. Verified token for token against the
  transformers.js reference and against the macOS implementation.
- `src/UploaderCli` – `cdu-cli` development tool (`fixture`, `analyze`,
  `roster`, `match`).
- `tests/UploaderCore.Tests` – unit tests.
- The WinUI 3 application is not in the repository yet.

## Building

Requirements: .NET 10 SDK.

```sh
dotnet test
dotnet run --project src/UploaderCli -- roster Q239447
```

The model (about 1 GB) is downloaded from Hugging Face on first use.

## Contributing

Commit messages follow the
[Wikimedia commit message guidelines](https://www.mediawiki.org/wiki/Gerrit/Commit_message_guidelines).

## License

[Apache License 2.0](LICENSE)
