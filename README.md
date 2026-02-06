# ArkTools

**A desktop tool for ARK: Survival Ascended server plugin development.**
Parses [AsaApi](https://github.com/ArkServerApi/AsaApi) C++ header files and generates ready-to-use hook code for the ASA Server API.

---

## What it does

AsaApi exposes game-engine internals through C++ headers (`Actor.h`, `GameMode.h`, `Inventory.h`, etc.).
Writing hooks by hand against these headers is repetitive and error-prone -- each hook requires:

1. A `DECLARE_HOOK` macro
2. A correctly typed implementation function
3. A `SetHook` registration call
4. A matching `DisableHook` teardown call

**ASA Hook Creator** reads those headers, extracts every struct, function signature, field, and bit-field, then produces all of that boilerplate automatically.

---

## Features

| Feature | Description |
|---|---|
| **Automatic loading** | On startup the tool downloads the six core AsaApi headers directly from GitHub. No local clone required. |
| **Local sources** | Load a single header file or an entire folder. Folder mode watches for file changes and reloads automatically. |
| **C++ parser** | Extracts structs, member functions (virtual / static), `GetNativePointerField`-style field accessors, and bit-fields. |
| **Full-text search** | Search across all parsed functions by name, class, return type, or source file. |
| **Per-function generation** | Select any function and generate `DECLARE_HOOK`, hook implementation, `SetHook`, and `DisableHook`. |
| **Batch generation** | Generate hooks for every function at once, including `SetupHooks()` and `RemoveHooks()` entry points. |
| **Copy / Save** | Copy generated code to the clipboard or save it to a `.h` / `.cpp` file. |

---

## Default header sources

Fetched on startup from the `master` branch of [ArkServerApi/AsaApi](https://github.com/ArkServerApi/AsaApi):

| Header | Contents |
|---|---|
| `Actor.h` | Base actor structs -- `AActor`, `APrimalCharacter`, etc. |
| `Buff.h` | Buff and status effect structs |
| `GameMode.h` | `AShooterGameMode` and related types |
| `Inventory.h` | Inventory component structs |
| `Other.h` | Miscellaneous game structs |
| `PrimalStructure.h` | Placeable structure structs |

---

## Requirements

- Windows 10 or later
- [.NET 10](https://dotnet.microsoft.com/download) runtime

---

## Build

```bash
dotnet build
```

---

## Tech stack

| Component | Library |
|---|---|
| UI | WPF + [WPF-UI Fluent](https://github.com/lepoco/wpfui) 3.x |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) 8.x |
| Target | .NET 10 (`net10.0-windows`) |

---

## License

This project is provided as-is for the ARK: Survival Ascended modding community.
