# Rfx2Mqtt — pont RFXCom ↔ MQTT

> 🇬🇧 **English version: [README.md](README.md)**

Un pont léger entre un **transceiver RFXCom 433 MHz** et un **broker MQTT**, avec interface
web Blazor Server pour la configuration et le monitoring. Construit sur .NET 9.

Reçoit les sondes Oregon / Bresser / Viking, pilote les volets Somfy RTS et les prises
Chacon/DIO, capte les détecteurs de mouvement X10 Security — le tout exposé en MQTT au
format Zigbee2MQTT compatible, avec auto-découverte Home Assistant optionnelle.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/docker-multi--arch-2496ED?logo=docker)](#docker)

---

## Fonctionnalités

- **RX capteurs** : sondes T°/humidité (Oregon, Bresser, TFA, Cresta, Viking, Rubicson…),
  détecteurs de mouvement X10 Security avec auto-reset anti-spam, sondes barométriques
  (BTHR918/968).
- **TX appareils** : volets Somfy RTS (up/down/stop/program), prises et dimmers Chacon/DIO
  (Lighting2 0x11).
- **Contrat MQTT** compatible avec les noms de champs Zigbee2MQTT (`battery`, `linkquality`,
  `occupancy`) — fonctionne immédiatement avec Home Assistant, Node-RED, DomoMud, etc.
- **MQTT Discovery Home Assistant** — entités créées automatiquement au démarrage.
- **UI Web** (Blazor + MudBlazor) : status, log de paquets, gestion d'appareils, paramètres
  MQTT/série, activation des protocoles, panneau d'auto-découverte pour les appareils non
  configurés.
- **Inventaire persistant** dans `data/devices.yaml` (éditable à la main en SSH), séparé de
  l'`appsettings.json` technique — voir la [philosophie](#pourquoi-yaml-pour-les-appareils).
- **Transport robuste** : resync série octet par octet sur le bruit, reconnexion MQTT automatique
  avec re-souscription aux commandes, suivi de disponibilité par capteur (LWT online/offline),
  timestamps en UTC (pas de flapping aux changements d'heure).
- **Builds portables** pour Linux x64, Linux ARM64 (Raspberry Pi) et Windows x64.
- **Image Docker** multi-arch (amd64 + arm64).

## Appareils supportés

| Famille | Direction | Protocole | Exemples |
|---|---|---|---|
| Sondes température / humidité | RX | Oregon, Bresser, Cresta, TFA, Viking | THGR810, Bresser Temeo, Viking 02035 |
| Sondes T° / humidité / baromètre | RX | Oregon | BTHR918, BTHR968 |
| Volets roulants | TX | Somfy RTS | Tous volets compatibles RFY |
| Prises / interrupteurs / dimmers | RX + TX | Chacon/DIO, HomeEasy EU, ANSLUT | Prises DI-O, interrupteurs RTS |
| Détecteurs de mouvement | RX | X10 Security, Visonic, KD101 | MS10, MS18, détecteurs de fumée |

## Installation

### Option 1 — Docker (recommandé)

Le moyen le plus rapide de démarrer sur un serveur domotique Linux.

```bash
# 1. Cloner
git clone https://github.com/krissam44/Rfx2Mqtt.git
cd Rfx2Mqtt

# 2. Préparer la config
mkdir -p config data
cp Rfx2Mqtt/appsettings.example.json config/appsettings.json
nano config/appsettings.json   # éditer l'IP du broker, port, identifiants, port série

# 3. Démarrer
docker compose up -d
docker compose logs -f
```

Ouvrir `http://<hôte>:5080` pour l'UI web.

Le `docker-compose.yml` monte :
- `config/appsettings.json` → `/app/appsettings.json` (écriture — l'UI peut sauvegarder les réglages)
- `data/` → `/app/data` (inventaire des appareils, en écriture)

### Option 2 — Build portable

Télécharger l'archive correspondant à votre plateforme depuis la page
[Releases](https://github.com/krissam44/Rfx2Mqtt/releases), ou la construire vous-même :

```bash
# Les trois plateformes en une commande (Linux x64, Linux ARM64, Windows x64)
pwsh scripts/publish-all.ps1
# ou
./scripts/publish-all.sh
```

Chaque archive est auto-contenue (pas besoin de .NET installé sur la machine cible). La sortie va
dans `release/Rfx2Mqtt-<version>-<rid>.zip`.

Sous Linux :
```bash
unzip Rfx2Mqtt-1.0.0-linux-arm64.zip -d rfx2mqtt && cd rfx2mqtt
chmod +x Rfx2Mqtt
nano appsettings.json
./Rfx2Mqtt
```

Pour le lancer en service systemd, voir [docs/systemd.md](#) (TODO).

### Option 3 — Compiler depuis les sources

```bash
git clone https://github.com/krissam44/Rfx2Mqtt.git
cd Rfx2Mqtt
dotnet run --project Rfx2Mqtt/Rfx2Mqtt.csproj
```

Nécessite le SDK .NET 9.

## Configuration

Deux fichiers, par conception (voir [Pourquoi YAML](#pourquoi-yaml-pour-les-appareils)).

### `appsettings.json` — paramètres techniques

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

> ⚠️ Sur Linux + systemd, préférer l'**adresse IP** du broker au hostname — la résolution DNS peut
> échouer silencieusement avant que la pile réseau soit complètement prête.

> ⚠️ `Lighting4` ET `X10` doivent être tous les deux activés pour recevoir les paquets X10
> Security Motion. Si un seul manque, c'est un échec silencieux.

### `data/devices.yaml` — inventaire des appareils

Auto-créé au premier démarrage. À éditer via l'UI web ou à la main :

```yaml
oregon:
  - name: Salon
    id: "0x710E"

security:
  - name: Mouvement Bureau
    id: "0x831480"

somfy:
  - name: volet_bureau
    id: "0 01 01"
    unit_code: 1
    sub_type: 0

chacon:
  - name: lumiere_cuisine
    id: "01 9E 75 0E"
    unit_code: 1
    sub_type: 0
```

## Topics MQTT

### Publiés

| Topic | Payload | Retain |
|---|---|---|
| `rfxcom/availability` | `{"state":"online\|offline"}` | oui (LWT) |
| `rfxcom/config/permit_join` | `{"state":"true\|false"}` | oui |
| `rfxcom/sensor/th/{nom}` | JSON `TempHumidityData` complet | oui |
| `rfxcom/sensor/th/{nom}/{attribut}` | `temperature`, `humidity`, `battery`, `signal` | oui |
| `rfxcom/sensor/th/{nom}/availability` | `{"state":"online\|offline"}` par capteur | oui |
| `rfxcom/sensor/thb/{nom}/barometer` | hPa en string | oui |
| `rfxcom/sensor/security/{nom}` | JSON `SecurityData` complet | oui |
| `rfxcom/sensor/security/{nom}/motion` | `"ON"\|"OFF"` | oui |
| `rfxcom/sensor/security/{nom}/occupancy` | `"ON"\|"OFF"` (alias Zigbee2MQTT) | oui |
| `rfxcom/sensor/chacon/{nom}` | JSON `Lighting2Data` complet | oui |
| `rfxcom/sensor/chacon/{nom}/state` | `"ON"\|"OFF"` | oui |
| `rfxcom/event/somfy/{nom}` | `{"command":"up\|down\|stop"}` (appuis télécommande) | non |

### Souscrits

| Topic | Action |
|---|---|
| `rfxcom/command/somfy/{nom}` | Envoyer commande Somfy RTS (`{ "command": "up\|down\|stop\|program" }`) |
| `rfxcom/command/chacon/{nom}` | Envoyer commande Lighting2 (`{ "command": "on\|off\|set_level", "level": 0..15 }`) |
| `rfxcom/command/restart` | Redémarrer le service |
| `rfxcom/command/permit_join` | Basculer le mode découverte (`true` / `false`) — persisté dans appsettings.json |

## Intégration

Rfx2Mqtt expose une interface MQTT stable, conçue pour être consommée par des plateformes
domotiques, des flows Node-RED ou tout bridge de protocole (ex : un bridge Matter, un plugin
GladysAssistant).

### Nom d'appareil → nom dans le topic

Le segment `{nom}` dans tous les topics correspond **verbatim** au champ `name` du fichier
`data/devices.yaml`. Les espaces et les accents sont conservés tels quels.

```yaml
oregon:
  - name: Salon          # → rfxcom/sensor/th/Salon
  - name: Chambre enfant # → rfxcom/sensor/th/Chambre enfant
```

> **Conseil pour un bridge** : utiliser le nom original pour les souscriptions MQTT ; ne le
> transformer en slug (`Chambre enfant` → `chambre-enfant`) que lors de l'enregistrement côté
> système cible.

### Payloads JSON complets

#### Sonde T°/Humidité — `rfxcom/sensor/th/{nom}` (retained)

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

| Champ | Type | Notes |
|---|---|---|
| `temperature` | `number` | °C |
| `humidity` | `integer` | 0–100 % |
| `humidityStatus` | `integer` | 0=Normal, 1=Confort, 2=Sec, 3=Humide |
| `barometer` | `number\|null` | hPa — uniquement pour les sondes THBaro (BTHR918/968), sinon `null` |
| `batteryLevel` | `integer` | 0–9 (0 = faible/vide, 9 = pleine) |
| `signalLevel` | `integer` | 0–15 |
| `channel` | `integer` | 1–3 (certains modèles Oregon) |

Publié également en topics plats individuels : `…/temperature`, `…/humidity`, `…/battery` (`"ok"` ou `"low"`), `…/barometer` (THBaro uniquement).

#### Détecteur de mouvement / Sécurité — `rfxcom/sensor/security/{nom}` (retained)

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

| Champ | Type | Notes |
|---|---|---|
| `motion` / `occupancy` | `boolean` | Même valeur — `occupancy` est l'alias Zigbee2MQTT |
| `status` | `string` | `motion`, `no_motion`, `alarm`, `alarm_delayed`, `normal`, `tamper`, `panic`… |
| `tamper` | `boolean` | Capteur ouvert ou arraché |
| `battery` | `integer` | 0–100 % (normalisé depuis `batteryLevel` 0–9) |
| `linkquality` | `integer` | 0–255 (normalisé depuis `signalLevel` 0–15, nom Zigbee2MQTT) |

> **Auto-reset** : après `MotionClearDelaySec` secondes de silence, `motion` et `occupancy`
> repassent à `false` automatiquement. Mettre à `0` pour désactiver.

#### Prise / interrupteur Chacon — `rfxcom/sensor/chacon/{nom}` (retained)

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

| Champ | Type | Notes |
|---|---|---|
| `state` | `"ON"\|"OFF"` | État actuel de la prise / interrupteur |
| `command` | `string` | `on`, `off`, `set_level`, `group_on`, `group_off` |
| `level` | `integer` | Niveau dimmer 0–15 (0 = éteint) |
| `levelPercent` | `integer` | 0–100 % |

#### Volet Somfy RTS — `rfxcom/event/somfy/{nom}` (**non retained**)

```json
{ "command": "up" }
```

Valeurs : `up`, `down`, `stop`, `program`. Émis lors d'un appui sur télécommande physique, non retained.

Pour envoyer une commande : publier `{ "command": "up" }` sur `rfxcom/command/somfy/{nom}`.

### Mapping Matter (pour les développeurs de bridge)

| Appareil Rfx2Mqtt | Cluster Matter | Attribut clé |
|---|---|---|
| Sonde Oregon TH | `TemperatureMeasurement` + `RelativeHumidityMeasurement` | `MeasuredValue` (°C × 100) |
| Sonde Oregon THBaro | + `PressureMeasurement` | `MeasuredValue` (hPa × 10) |
| Détecteur X10 Security | `OccupancySensing` | `Occupancy` ← champ `occupancy` |
| Prise Chacon | `OnOff` | `OnOff` ← `state == "ON"` |
| Volet Somfy | `WindowCovering` | `UpOrOpen` / `DownOrClose` / `StopMotion` |

Un bridge souscrit à `rfxcom/sensor/#` et `rfxcom/event/#`, lit `data/devices.yaml` pour
énumérer les appareils connus, et mappe chaque entrée vers un bridged device Matter selon la
table ci-dessus. Les commandes circulent en sens inverse : Matter → publication sur
`rfxcom/command/{type}/{nom}`.

## UI Web

Pages disponibles sur `http://<hôte>:5080` :

| Page | Rôle |
|---|---|
| **Status** | Log de paquets en temps réel + connectivité du bridge |
| **Découverte** | Auto-découverte — capter les appareils non configurés et les ajouter |
| **Appareils** | Gérer les appareils Oregon / Security / Somfy / Chacon |
| **Connexion** | Paramètres broker MQTT + port série |
| **Protocoles** | Activer/désactiver les protocoles RF via bitmask |

## Pourquoi YAML pour les appareils ?

`appsettings.json` = **application** (broker, port, protocoles — modifié rarement par un
développeur). `data/devices.yaml` = **inventaire** (modifié souvent via UI ou SSH par un
opérateur). Les mélanger brouille la frontière, alourdit les diffs Git et fragilise les
déploiements.

C'est le pattern utilisé par Zigbee2MQTT et Home Assistant — éprouvé dans le monde domotique.

Une migration ponctuelle des sections d'appareils d'`appsettings.json` vers `devices.yaml` a
lieu au premier démarrage ; un `.bak` de l'original est conservé.

## Structure du projet

```
Rfx2Mqtt/
├── Configuration/        # Options, repository d'appareils, état PermitJoin
├── Devices/
│   ├── Handlers/         # IPacketHandler + TempHumidity/Security/Somfy/Lighting2
│   └── Models/           # Données décodées + constantes de sous-types
├── Discovery/            # HomeAssistantDiscoveryService, AvailabilityService
├── Mqtt/                 # MqttService + helper MqttTopics
├── Serial/               # RfxComSerialService, RfxComProtocol, PacketEventArgs
├── UI/                   # Pages Blazor Server + dialogs
│   └── Components/Pages/ # Status, Decouverte, Appareils, Connexion, Protocoles
├── Worker.cs             # Orchestrateur principal IHostedService
└── Program.cs            # Setup DI / hébergement
```

## Contribuer

Les bug reports et PRs sont les bienvenus. Le code est intégralement bilingue (XML doc EN/FR) et
suit les patterns documentés dans [`CLAUDE.md`](CLAUDE.md).

Dev local :
```bash
dotnet watch --project Rfx2Mqtt/Rfx2Mqtt.csproj
```

## Licence

[MIT](LICENSE) — © 2026 Christian Sammut.

## Remerciements

- L'équipe [RFXCOM](http://www.rfxcom.com/) pour le dongle et le PDF du SDK.
- [MQTTnet](https://github.com/dotnet/MQTTnet) pour le client MQTT.
- [MudBlazor](https://mudblazor.com/) pour cette superbe librairie de composants Blazor.
- Le projet [Zigbee2MQTT](https://www.zigbee2mqtt.io/) pour avoir montré ce à quoi ressemble un
  bon découpage config/inventaire.
- [Pierre-Gilles Leymarie](https://github.com/pierre-gilles-leymarie) de
  [GladysAssistant](https://gladysassistant.com/) pour l'idée du bridge Matter et son
  engagement dans la communauté.
