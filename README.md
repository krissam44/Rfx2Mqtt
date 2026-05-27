# Rfx2Mqtt — RFXCom ↔ MQTT bridge

> 🇫🇷 **Version française : [README.fr.md](README.fr.md)**

A lightweight bridge between an **RFXCom 433 MHz transceiver** and any **MQTT broker**, with a
Blazor Server web UI for configuration and monitoring. Built on .NET 9.

Receives data from Oregon / Bresser / Viking weather probes, controls Somfy RTS blinds and
Chacon/DIO outlets, picks up X10 Security motion detectors — all exposed on MQTT in a
Zigbee2MQTT-friendly format, with optional Home Assistant MQTT Discovery.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/docker-multi--arch-2496ED?logo=docker)](#docker)

---

## Features

- **Sensor RX**: temperature/humidity probes (Oregon, Bresser, TFA, Cresta, Viking, Rubicson…),
  X10 Security motion detectors with anti-spam auto-reset, barometric probes (BTHR918/968).
- **Device TX**: Somfy RTS blinds (up/down/stop/program), Chacon/DIO outlets and dimmers
  (Lighting2 0x11).
- **MQTT contract** compatible with Zigbee2MQTT field names (`battery`, `linkquality`,
  `occupancy`) so existing consumers (Home Assistant, Node-RED, DomoMud…) work out of the box.
- **Home Assistant MQTT Discovery** — entities created automatically on startup.
- **Web UI** (Blazor + MudBlazor): status, packet log, device list, MQTT/serial settings,
  protocol activation, auto-discovery panel for unconfigured devices.
- **Persistent inventory** in `data/devices.yaml` (human-editable in SSH), separated from the
  technical `appsettings.json` — see the [philosophy](#why-yaml-for-devices) below.
- **Robust transport**: serial re-sync byte by byte on noise, MQTT auto-reconnect with command
  re-subscription, sensor availability tracker (online/offline LWT per device), UTC-based
  timestamps (no DST flapping).
- **Portable builds** for Linux x64, Linux ARM64 (Raspberry Pi) and Windows x64.
- **Docker** image multi-arch (amd64 + arm64).

## Supported devices

| Family | Direction | Protocol | Examples |
|---|---|---|---|
| Temperature / humidity probes | RX | Oregon, Bresser, Cresta, TFA, Viking | THGR810, Bresser Temeo, Viking 02035 |
| Temp / humidity / barometer probes | RX | Oregon | BTHR918, BTHR968 |
| Roller blinds | TX | Somfy RTS | All RFY-compatible blinds |
| Outlets / switches / dimmers | RX + TX | Chacon/DIO, HomeEasy EU, ANSLUT | DI-O outlets, RTS-aware switches |
| Motion detectors | RX | X10 Security, Visonic, KD101 | MS10, MS18, smoke detectors |

## Installation

### Option 1 — Docker (recommended)

The fastest way to get running on a Linux home server.

```bash
# 1. Clone
git clone https://github.com/krissam44/Rfx2Mqtt.git
cd Rfx2Mqtt

# 2. Prepare your config
mkdir -p config data
cp Rfx2Mqtt/appsettings.example.json config/appsettings.json
nano config/appsettings.json   # edit broker IP, port, credentials, serial port

# 3. Start
docker compose up -d
docker compose logs -f
```

Open `http://<host>:5080` for the web UI.

The `docker-compose.yml` mounts:
- `config/appsettings.json` → `/app/appsettings.json` (writable — UI can persist settings)
- `data/` → `/app/data` (device inventory, writable)

### Option 2 — Portable build

Download the matching archive from the [Releases](https://github.com/krissam44/Rfx2Mqtt/releases)
page, or build it yourself:

```bash
# All three platforms at once (Linux x64, Linux ARM64, Windows x64)
pwsh scripts/publish-all.ps1
# or
./scripts/publish-all.sh
```

Each archive is self-contained (no .NET install required on the target machine). Output goes to
`release/Rfx2Mqtt-<version>-<rid>.zip`.

On Linux:
```bash
unzip Rfx2Mqtt-1.0.0-linux-arm64.zip -d rfx2mqtt && cd rfx2mqtt
chmod +x Rfx2Mqtt
nano appsettings.json
./Rfx2Mqtt
```

To run as a systemd service, see [docs/systemd.md](#) (TODO).

### Option 3 — Build from source

```bash
git clone https://github.com/krissam44/Rfx2Mqtt.git
cd Rfx2Mqtt
dotnet run --project Rfx2Mqtt/Rfx2Mqtt.csproj
```

Requires .NET 9 SDK.

## Configuration

Two files, by design (see [Why YAML for devices](#why-yaml-for-devices)).

### `appsettings.json` — technical settings

```json
{
  "RfxCom": {
    "PortName": "/dev/ttyUSB0",
    "BaudRate": 38400,
    "PermitJoin": true,
    "AvailabilityTimeoutSec": 300,
    "MotionClearDelaySec": 120,
    "Protocols": {
      "OregonScientific": true,
      "Lighting4": true,
      "X10": true,
      "BlindsT1T2T3T4": true,
      "Rubicson": true,
      "Undecoded": false
    }
  },
  "Mqtt": {
    "Host": "192.168.1.4",
    "Port": 1883,
    "Username": "",
    "Password": "",
    "ClientId": "Rfx2Mqtt",
    "TopicPrefix": "rfxcom"
  }
}
```

> ⚠️ On Linux + systemd, prefer the broker **IP address** over a hostname — DNS resolution can
> fail silently before the network stack is fully up.

> ⚠️ `Lighting4` AND `X10` must both be enabled to receive X10 Security Motion packets. Missing
> either is a silent failure.

### `data/devices.yaml` — device inventory

Auto-created on first startup. Edit via the web UI or by hand:

```yaml
oregon:
  - name: Living room
    id: "0x710E"

security:
  - name: Office motion
    id: "0x831480"

somfy:
  - name: office_blind
    id: "0 01 01"
    unit_code: 1
    sub_type: 0

chacon:
  - name: kitchen_light
    id: "01 9E 75 0E"
    unit_code: 1
    sub_type: 0
```

## MQTT topics

### Published

| Topic | Payload | Retained |
|---|---|---|
| `rfxcom/availability` | `{"state":"online\|offline"}` | yes (LWT) |
| `rfxcom/config/permit_join` | `{"state":"true\|false"}` | yes |
| `rfxcom/sensor/th/{name}` | full `TempHumidityData` JSON | yes |
| `rfxcom/sensor/th/{name}/{attribute}` | one of `temperature`, `humidity`, `battery`, `signal` | yes |
| `rfxcom/sensor/th/{name}/availability` | `{"state":"online\|offline"}` per sensor | yes |
| `rfxcom/sensor/thb/{name}/barometer` | hPa string | yes |
| `rfxcom/sensor/security/{name}` | full `SecurityData` JSON | yes |
| `rfxcom/sensor/security/{name}/motion` | `"ON"\|"OFF"` | yes |
| `rfxcom/sensor/security/{name}/occupancy` | `"ON"\|"OFF"` (Zigbee2MQTT alias) | yes |
| `rfxcom/sensor/chacon/{name}` | full `Lighting2Data` JSON | yes |
| `rfxcom/sensor/chacon/{name}/state` | `"ON"\|"OFF"` | yes |
| `rfxcom/event/somfy/{name}` | `{"command":"up\|down\|stop"}` (remote presses) | no |

### Subscribed

| Topic | Action |
|---|---|
| `rfxcom/command/somfy/{name}` | Send Somfy RTS command (payload `{ "command": "up\|down\|stop\|program" }`) |
| `rfxcom/command/chacon/{name}` | Send Lighting2 command (`{ "command": "on\|off\|set_level", "level": 0..15 }`) |
| `rfxcom/command/restart` | Restart the service |
| `rfxcom/command/permit_join` | Toggle discovery mode (`true` / `false`) — persisted to appsettings.json |

## Integration

Rfx2Mqtt exposes a stable MQTT interface designed to be consumed by home automation platforms,
Node-RED flows, or any protocol bridge (e.g. a Matter bridge, a GladysAssistant plugin).

### Device name → topic name

The `{name}` segment in all topics maps **verbatim** to the `name` field in `data/devices.yaml`.
Spaces and accents are preserved exactly as typed.

```yaml
oregon:
  - name: Living room    # → rfxcom/sensor/th/Living room
  - name: Garage         # → rfxcom/sensor/th/Garage
```

> **Bridge tip**: use the original name for MQTT subscriptions; slugify it only when registering
> in the target system (`Living room` → `living-room`).

### Full JSON payloads

#### Temperature / Humidity — `rfxcom/sensor/th/{name}` (retained)

```json
{
  "sensorType": "0x02",
  "sensorId": "0x710E",
  "sensorModel": "Oregon THGR810/THGN800",
  "temperature": 21.4,
  "humidity": 56,
  "humidityStatus": 1,
  "barometer": null,
  "batteryLevel": 9,
  "signalLevel": 7,
  "channel": 1,
  "receivedAt": "2026-05-28T14:23:11.456Z"
}
```

| Field | Type | Notes |
|---|---|---|
| `temperature` | `number` | °C |
| `humidity` | `integer` | 0–100 % |
| `humidityStatus` | `integer` | 0=Normal, 1=Comfort, 2=Dry, 3=Wet |
| `barometer` | `number\|null` | hPa — only for THBaro probes (BTHR918/968), `null` otherwise |
| `batteryLevel` | `integer` | 0–9 (0 = low/empty, 9 = full) |
| `signalLevel` | `integer` | 0–15 |
| `channel` | `integer` | 1–3 (some Oregon models) |

Also published as individual flat topics: `…/temperature`, `…/humidity`, `…/battery` (`"ok"` or `"low"`), `…/barometer` (THBaro only).

#### Motion / Security — `rfxcom/sensor/security/{name}` (retained)

```json
{
  "sensorType": "0x01",
  "sensorModel": "X10 Security Motion",
  "sensorId": "0x831480",
  "status": "motion",
  "motion": true,
  "tamper": false,
  "batteryLevel": 9,
  "signalLevel": 6,
  "battery": 100,
  "linkquality": 102,
  "occupancy": true,
  "receivedAt": "2026-05-28T14:23:11.456Z"
}
```

| Field | Type | Notes |
|---|---|---|
| `motion` / `occupancy` | `boolean` | Same value — `occupancy` is the Zigbee2MQTT alias |
| `status` | `string` | `motion`, `no_motion`, `alarm`, `alarm_delayed`, `normal`, `tamper`, `panic`… |
| `tamper` | `boolean` | Sensor opened or physically removed |
| `battery` | `integer` | 0–100 % (normalized from `batteryLevel` 0–9) |
| `linkquality` | `integer` | 0–255 (normalized from `signalLevel` 0–15, Zigbee2MQTT name) |

> **Auto-reset**: after `MotionClearDelaySec` seconds of silence, `motion` and `occupancy` revert
> to `false` automatically. Set to `0` to disable.

#### Chacon / DIO outlet — `rfxcom/sensor/chacon/{name}` (retained)

```json
{
  "sensorType": "0x00",
  "model": "Chacon/DIO/AC",
  "deviceId": "0x019E750E",
  "unitCode": 1,
  "command": "on",
  "state": "ON",
  "level": 15,
  "levelPercent": 100,
  "signalLevel": 7,
  "receivedAt": "2026-05-28T14:23:11.456Z"
}
```

| Field | Type | Notes |
|---|---|---|
| `state` | `"ON"\|"OFF"` | Current outlet / switch state |
| `command` | `string` | `on`, `off`, `set_level`, `group_on`, `group_off` |
| `level` | `integer` | Dimmer level 0–15 (0 = off) |
| `levelPercent` | `integer` | 0–100 % |

#### Somfy RTS blind — `rfxcom/event/somfy/{name}` (**not retained**)

```json
{ "command": "up" }
```

Values: `up`, `down`, `stop`, `program`. Fired on physical remote press, not retained.

To send a command: publish `{ "command": "up" }` to `rfxcom/command/somfy/{name}`.

### Matter mapping (for bridge implementors)

| Rfx2Mqtt device | Matter cluster | Key attribute |
|---|---|---|
| Oregon TH probe | `TemperatureMeasurement` + `RelativeHumidityMeasurement` | `MeasuredValue` (°C × 100) |
| Oregon THBaro | + `PressureMeasurement` | `MeasuredValue` (hPa × 10) |
| X10 Security motion | `OccupancySensing` | `Occupancy` ← `occupancy` field |
| Chacon outlet | `OnOff` | `OnOff` ← `state == "ON"` |
| Somfy blind | `WindowCovering` | `UpOrOpen` / `DownOrClose` / `StopMotion` |

A bridge subscribes to `rfxcom/sensor/#` and `rfxcom/event/#`, reads `data/devices.yaml` to
enumerate known devices, and maps each entry to a Matter bridged device using the table above.
Commands flow in the opposite direction: Matter → publish to `rfxcom/command/{kind}/{name}`.

## Web UI

Pages available at `http://<host>:5080`:

| Page | Purpose |
|---|---|
| **Status** | Real-time packet log + bridge connectivity |
| **Découverte** | Auto-discovery — pick up unconfigured devices and add them |
| **Appareils** | Manage Oregon / Security / Somfy / Chacon devices |
| **Connexion** | MQTT broker + serial port settings |
| **Protocoles** | Activate / deactivate RF protocols via bitmask |

## Why YAML for devices?

`appsettings.json` is for the **application** (broker, port, protocols — modified rarely by a
developer). `data/devices.yaml` is for the **inventory** (modified often via UI or SSH by an
operator). Mixing them blurs the boundary, bloats Git diffs and makes deployments fragile.

The pattern matches what Zigbee2MQTT and Home Assistant do — proven in the smart-home space.

A one-shot migration from `appsettings.json` device sections to `devices.yaml` happens on first
start; a `.bak` of the original is kept.

## Project layout

```
Rfx2Mqtt/
├── Configuration/        # Options, device repository, PermitJoin state
├── Devices/
│   ├── Handlers/         # IPacketHandler + TempHumidity/Security/Somfy/Lighting2
│   └── Models/           # Decoded packet data + subtype constants
├── Discovery/            # HomeAssistantDiscoveryService, AvailabilityService
├── Mqtt/                 # MqttService + MqttTopics helper
├── Serial/               # RfxComSerialService, RfxComProtocol, PacketEventArgs
├── UI/                   # Blazor Server pages + dialogs
│   └── Components/Pages/ # Status, Decouverte, Appareils, Connexion, Protocoles
├── Worker.cs             # Main IHostedService orchestrator
└── Program.cs            # DI / hosting setup
```

## Contributing

Bug reports and PRs welcome. The codebase is fully bilingual (EN/FR XML docs) and follows the
patterns documented in [`CLAUDE.md`](CLAUDE.md).

Local dev:
```bash
dotnet watch --project Rfx2Mqtt/Rfx2Mqtt.csproj
```

## License

[MIT](LICENSE) — © 2026 Christian Sammut.

## Acknowledgements

- The [RFXCOM](http://www.rfxcom.com/) team for the dongle and the SDK PDF.
- [MQTTnet](https://github.com/dotnet/MQTTnet) for the MQTT client.
- [MudBlazor](https://mudblazor.com/) for the gorgeous Blazor component library.
- The [Zigbee2MQTT](https://www.zigbee2mqtt.io/) project for showing what a great
  config/inventory split looks like.
- [Pierre-Gilles Leymarie](https://github.com/pierre-gilles-leymarie) from
  [GladysAssistant](https://gladysassistant.com/) for the Matter bridge idea and community
  engagement.
