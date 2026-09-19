# Importing the current local source

The original development folders contain generated binaries, IDE state, backups and other material that should not be published.

Use the repository importer instead of dragging the complete folders into GitHub.

## What is imported

### Firmware

- `CMakeLists.txt`
- `pico_sdk_import.cmake`
- `src/`
- `tests/`
- `diagnostics/`
- `tools/`
- `docs/`
- `linker/`

### Windows application

- `StreamDeckDIY.sln`
- `StreamDeckDIY.App/`
- `StreamDeckDIY.Core/`
- `StreamDeckDIY.Protocol/`
- `StreamDeckDIY.Protocol.Tests/`
- `StreamDeckDIY.Transport/`
- `docs/`

The importer removes generated `bin/`, `obj/`, `.vs/` and `artifacts/` directories defensively.

It intentionally does **not** import:

- firmware `build/` or `build-host/`;
- firmware backups;
- local VS Code configuration;
- internal `AGENTS.md` files;
- UI concept-work folders;
- arbitrary executables/build products.

## Usage

From a fresh clone of this repository:

```powershell
.\tools\import-local-source.ps1 `
  -FirmwarePath "PATH_TO_STREAMDECK_FIRMWARE" `
  -AppPath "PATH_TO_STREAMDECKDIY_APP"
```

Then review the result:

```powershell
git status
git diff --stat
```

If everything is correct:

```powershell
git add .
git commit -m "feat: publish firmware and Windows app source"
git push
```

## CAD

The supplied 3MF contains useful geometry but also slicer/printer project metadata. It is therefore excluded by default.

To include it deliberately:

```powershell
.\tools\import-local-source.ps1 `
  -FirmwarePath "PATH_TO_STREAMDECK_FIRMWARE" `
  -AppPath "PATH_TO_STREAMDECKDIY_APP" `
  -IncludeCad3mf
```

For a polished public release, a geometry-focused STEP/STL/3MF export is preferable to a slicer project file.
