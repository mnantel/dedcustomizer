# WINCTRL DED Customizer

Drive a **WinWing F-16 ICP/DED** replica from live **DCS World** flight data.
Configure exactly what each of the five DED lines shows, per aircraft, using a
friendly system-tray GUI — or run headless as a background service.

```
┌──────────────────────────┐
│  HDG 270   ALT 18500 FT  │  ← custom line layout
│  UHF 225.000   320KT     │
│  STPT BULLS              │
│  AM 118.000  FM 30.000   │
│  FUEL 11240 LBS          │
└──────────────────────────┘
```

---

## Features

- **Per-aircraft profiles** — configure a unique layout for every DCS module
- **Field catalog** — define named fields (alias → DCS parameter key + format template)
- **Live DCS field browser** — browse all available cockpit parameters from a running DCS session and import them with one click
- **Format templates** — `%XXX%` integer, `%XXX.XXX%` float, `%SSSSSSS%` string, `%B%` boolean
- **System tray GUI** — green/yellow/red status icons, deploy Lua script from the app, no terminal needed
- **Headless mode** — `DcsDedBridge` runs without a GUI for always-on setups
- **Auto-migration** — existing config files are upgraded automatically on load

---

## Requirements

| Requirement | Details |
|---|---|
| **Hardware** | WinWing F-16 ICP/DED (USB HID, VID `0x4098` PID `0xbf06`) |
| **Simulator** | DCS World (any version with `LuaExportActivityNextEvent` support) |
| **Runtime** | [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9) (for GUI) or .NET 9 (for headless) |
| **OS** | Windows 10/11 |

---

## Getting Started

### 1 — Build

```
dotnet build DedCustomizer.sln -c Release
```

### 2 — Deploy the DCS Lua script

Open **WINCTRL DED Customizer** from the system tray.
Go to the **Status** tab and click **Deploy script**.

This installs `ded_bridge.lua` into:
```
%USERPROFILE%\Saved Games\DCS.openbeta\Scripts\ded_bridge\
```
and hooks `Export.lua` automatically.

Alternatively, copy `scripts/ded_bridge.lua` manually and add to your `Export.lua`:
```lua
local ded = require("Scripts.ded_bridge.ded_bridge")
```

### 3 — Run

**GUI (recommended):**
```
DcsDedGui\bin\Release\net9.0-windows\DcsDedGui.exe
```
The app sits in the system tray. Click the icon to open the configuration window.

**Headless:**
```
DcsDedBridge\bin\Release\net9.0\DcsDedBridge.exe
```

**Test mode (no DCS or hardware needed):**
```
DcsDedBridge.exe --test
```

---

## Configuration

The app stores its config at `%APPDATA%\DcsDedBridge\config.json`.

### Aircraft Profiles

Each aircraft can be set to one of three modes:

| Mode | Behaviour |
|---|---|
| **Default** | Built-in layout: HDG/ALT, UHF/IAS, STPT, AM/FM, FUEL |
| **Skip** | Ignore this aircraft — don't update the display |
| **Custom** | Use your own 5-line token layout |

### Field Catalog

Fields are named entries that map an alias to a DCS parameter key plus a format
template. The catalog is shared across all aircraft.

| Alias | DCS Key | Default Format |
|---|---|---|
| `HDG` | `hdg_deg` | `HDG %XXX%` |
| `ALT` | `alt_ft` | `ALT %XXXXX% FT` |
| `IAS` | `ias_kt` | `%XXX%KT` |
| `UHF` | `uhf_mhz` | `UHF %XXX.XXX%` |
| `AM` | `vhfam_mhz` | `AM %XXX.XXX%` |
| `FM` | `vhffm_mhz` | `FM %XXX.XXX%` |
| `FUEL` | `fuel_lbs` | `FUEL %XXXXX% LBS` |
| `STPT` | `stpt_name` | `%SSSSSSSSSSSSSSSSSSSSSSSS%` |

You can add any additional fields using **Browse DCS Fields** while DCS is running.

### Format Templates

Format strings mix literal text with one or more `%placeholder%` tokens:

| Token | Meaning | Example |
|---|---|---|
| `%X%` … `%XXXXXXXXX%` | Right-justified integer (N digits, truncate left on overflow) | `HDG %XXX%` → `HDG 270` |
| `%X.X%` … `%XXX.XXX%` | Float: N integer digits, M decimal places | `UHF %XXX.XXX%` → `UHF 225.000` |
| `%S%` … `%SSSSSSSSSSSSSSSSSSSSSSSS%` | Left-justified string, truncate right (N chars) | `%SSSSSSSSSS%` → `BULLS     ` |
| `%B%` | Boolean: `ON ` or `OFF` (always 3 chars) | `GEAR %B%` → `GEAR ON ` |

Each DED line is 24 characters wide. The GUI shows a live character-count indicator.

### Line Tokens

Each custom line is a list of **tokens** — either a field alias or a separator:

- **Field token** — renders the field's formatted value
- **Separator token** — renders a literal string (spaces, pipes, dashes, etc.)

---

## Architecture

```
┌─ DCS World ────────────────────────────────────┐
│  ded_bridge.lua  (Lua export script)           │
│  ├─ Parses cockpit params @ ~10 Hz            │
│  ├─ Computes derived values                   │
│  │   (radians→deg, m→ft, kg→lbs, m/s→kts)    │
│  └─ Sends JSON → UDP 127.0.0.1:7778           │
└────────────────────────────────────────────────┘
                        │ UDP
            ┌───────────▼───────────┐
            │  DcsDedBridge (CLI)   │
            │  or DcsDedGui (tray)  │
            │                       │
            │  Parse JSON           │
            │  Look up profile      │
            │  Render 5 lines       │
            └───────────┬───────────┘
                        │ HID commands
            ┌───────────▼───────────┐
            │   DedSharp library    │
            │  (USB HID driver)     │
            └───────────┬───────────┘
                        │ USB
            ┌───────────▼───────────┐
            │  WinWing F-16 DED     │
            │  200×65 px display    │
            └───────────────────────┘
```

### Projects

| Project | Type | Description |
|---|---|---|
| `DedSharp` | net8.0 class library | USB HID driver for WinWing ICP/DED hardware |
| `DcsDedShared` | net9.0 class library | Config model, field catalog, line renderer |
| `DcsDedBridge` | net9.0 console app | Headless UDP→DED bridge |
| `DcsDedGui` | net9.0-windows WPF | System-tray configuration + monitoring app |

### Key Source Files

```
DedSharp/
  IcpHidDevice.cs          Low-level USB HID via HidSharp
  DedDevice.cs             Pixel-buffer generation + command dispatch
  BmsDedDisplayProvider.cs Text-to-bitmap with 8×13 glyph font

DcsDedShared/
  LineRenderer.cs          Format-template engine + default field definitions
  ConfigStore.cs           JSON persistence + auto-migration
  DedConfig.cs             Root config model
  FieldDefinition.cs       Field catalog entry (alias, key, format, sample)
  DedLineToken.cs          Line token (field ref or separator text)
  KnownAircraft.cs         DCS module name autocomplete list

DcsDedBridge/
  Program.cs               UDP listener + render loop

DcsDedGui/
  TrayApp.cs               NotifyIcon, context menu, lifecycle
  BridgeService.cs         Background UDP + HID service, INotifyPropertyChanged
  ConfigWindow.xaml/.cs    Status panel + aircraft editor + field catalog UI
  LineEditorViewModel.cs   Token chip MVVM for 5-line editor
  FieldBrowserWindow.xaml  Live DCS parameter browser dialog
  DcsSetup.cs              Lua script deploy + version check

scripts/
  ded_bridge.lua           DCS export script (also embedded in DedBridgeLua.cs)
```

---

## How It Works

### Lua Script

`ded_bridge.lua` hooks into `LuaExportActivityNextEvent` (called by DCS at ~10 Hz).
It chains safely with any existing export hooks. Each tick it:

1. Reads `list_cockpit_params()` — raw sensor values
2. Reads `list_indication()` — scratchpad / UFC text panels
3. Computes derived values (heading in degrees, altitude in feet, fuel in lbs, IAS in knots)
4. Sends a JSON object over UDP to `127.0.0.1:7778`

The JSON includes both named convenience fields (`hdg_deg`, `alt_ft`, …) and all
raw cockpit parameter keys (`BASE_SENSOR_*`, `UHF_FREQ`, etc.) so any parameter
can be added to the field catalog.

### Rendering

`LineRenderer.RenderLine()` processes a list of `DedLineToken`s:

- **Field tokens** — looks up the `FieldDefinition`, reads the value from the
  received JSON dict, and formats it using `RenderValue()`.
- **Separator tokens** — appends `SeparatorText` verbatim.

If a field key isn't present in the current JSON snapshot (e.g., DCS not
connected), the field's stored `Sample` value is used as a preview fallback.

Each line is padded/truncated to exactly 24 characters before being sent to
`BmsDedDisplayProvider`, which renders the characters as 8×13 bitmaps and
updates the physical display.

---

## Config File Format

`%APPDATA%\DcsDedBridge\config.json`:

```json
{
  "defaultMode": "Default",
  "dcsSavedGamesDir": null,
  "fields": [
    { "alias": "HDG",  "key": "hdg_deg",    "format": "HDG %XXX%",         "sample": "270"     },
    { "alias": "ALT",  "key": "alt_ft",     "format": "ALT %XXXXX% FT",    "sample": "18500"   },
    { "alias": "IAS",  "key": "ias_kt",     "format": "%XXX%KT",            "sample": "320"     },
    { "alias": "UHF",  "key": "uhf_mhz",    "format": "UHF %XXX.XXX%",     "sample": "225.000" },
    { "alias": "AM",   "key": "vhfam_mhz",  "format": "AM %XXX.XXX%",      "sample": "118.000" },
    { "alias": "FM",   "key": "vhffm_mhz",  "format": "FM %XXX.XXX%",      "sample": "30.000"  },
    { "alias": "FUEL", "key": "fuel_lbs",   "format": "FUEL %XXXXX% LBS",  "sample": "11240"   },
    { "alias": "STPT", "key": "stpt_name",  "format": "%SSSSSSSSSSSSSSSSSSSSSSSS%", "sample": "BULLS" }
  ],
  "planes": [
    {
      "name": "F-16C_50",
      "mode": "Custom",
      "lines": [
        [ { "alias": "HDG" }, { "alias": null, "separatorText": "   " }, { "alias": "ALT" } ],
        [ { "alias": "UHF" }, { "alias": null, "separatorText": "  " },  { "alias": "IAS" } ],
        [ { "alias": "STPT" } ],
        [ { "alias": "AM" },  { "alias": null, "separatorText": "  " },  { "alias": "FM" }  ],
        [ { "alias": "FUEL" } ]
      ]
    }
  ]
}
```

---

## Hardware Notes

The WinWing F-16 DED communicates over USB HID:

- **Vendor ID:** `0x4098`
- **Product ID:** `0xbf06`
- **Display:** 200 × 65 pixels = 24 columns × 5 rows (8 × 13 px glyphs)

`DedSharp` uses [HidSharp](https://github.com/nicowillis/HidSharp) for
cross-platform HID access and `System.Drawing.Common` for bitmap rendering of
the glyph font.

---

## License

MIT — see [LICENSE](LICENSE).
