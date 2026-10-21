# Hosting a Seraph Horizons server

How to run a dedicated server with the pack. Generic server setup (ports, configuration, admin
rights, running as a service) is covered by the Vintage Story wiki, and this page links to it
rather than repeating it:

- [Guide: Dedicated Server](https://wiki.vintagestory.at/Guide:Dedicated_Server)
- [Setting up a Multiplayer Server](https://wiki.vintagestory.at/Setting_up_a_Multiplayer_Server)
- [Server Config](https://wiki.vintagestory.at/Server_Config),
  [List of server commands](https://wiki.vintagestory.at/List_of_server_commands),
  [Troubleshooting multiplayer](https://wiki.vintagestory.at/Troubleshooting_multiplayer)

Every release pins one game version. It is the **Game version** in [README.md](README.md)'s status
line (and `game_version` in [`pack/pack.toml`](pack/pack.toml)); at the time of writing, 1.22.7
on .NET 10. The server must run exactly that version.

There are two ways to install. Both use files from a [GitHub Release](../../releases):

| Route | Platforms | Release file |
|---|---|---|
| [cairn-server](#cairn-server-recommended) (recommended) | Linux x64 | `seraphhorizons_<v>.cairn` |
| [Plain dedicated server](#plain-dedicated-server) | Linux x64, Windows | `seraphhorizons_<v>_server.zip` |

## Hardware

The wiki's guidance for vanilla is 1 GB of RAM plus about 300 MB per player
([Technical specifications](https://wiki.vintagestory.at/Guide:Dedicated_Server#Technical_specifications)).
The pack runs 128 mods and needs more. As a rough estimate, not a measurement: at least 4 GB for
the server plus about 300 MB per player, and more while new terrain is generating.

## cairn-server (recommended)

[Cairn](https://github.com/cairns-gg/cairn-app)'s `cairn-server` installs the dedicated server
of the pack's game version and a private .NET 10 by itself, then downloads every mod in the
pack's lock and checks each one's sha256. It runs on Linux x64 only.

CI installs every build of the pack with cairn-server 0.9.10 (`CAIRN_VERSION` in
[`.github/workflows/ci.yml`](.github/workflows/ci.yml)), so that is the version the pack is tested
with.

1. Download cairn-server from <https://cairns.gg/download/linux> and extract it.
2. Download `seraphhorizons_<v>.cairn` from the release and install it:

   ```sh
   ./cairn-server install seraphhorizons_<v>.cairn
   ```

   `install` also takes the pack file's URL in place of a local file. It prints the pack's id
   (`seraphhorizons`), which the other commands take.
3. Start the server:

   ```sh
   ./cairn-server run seraphhorizons
   ```

   The first start creates the world (see [The world](#the-world)).

Other commands:

| Command | Does |
|---|---|
| `cairn-server list` | Lists installed packs |
| `cairn-server update [<id>]` | Takes a newer revision of a pack followed from an address; a running server keeps its mods until restarted |
| `cairn-server command [<id>] "/whitelist add name"` | Sends a command to the running server |
| `cairn-server unit [<id>] [--user] [--write]` | Writes a systemd template unit, `cairn-server@.service` |
| `cairn-server version` | Prints its version |

`unit` prints the `systemctl` and `useradd` commands to run rather than running them. The system
unit runs as user `cairn` with `CAIRN_HOME=/var/lib/cairn`. For a `--user` unit to keep running
after you log out, run `sudo loginctl enable-linger $USER`.

Cairn keeps its data in `~/.cairn`, or in `$CAIRN_HOME` when that is set. A pack's mods are in
`packs/<id>/Mods` and its server data (`Saves/`, `ModConfig/`, `Playerdata/`) in
`packs/<id>/data`. Cairn's own documentation is in its repository:
[game installs](https://github.com/cairns-gg/cairn-app/blob/main/docs/game-installs.md) and
[pack isolation](https://github.com/cairns-gg/cairn-app/blob/main/docs/pack-isolation.md).

Open port 42420 (TCP and UDP) as for any server
([Opening the firewall](https://wiki.vintagestory.at/Guide:Dedicated_Server#Opening_the_firewall)).

## Plain dedicated server

1. Install the vanilla dedicated server of the pack's game version, following the wiki:
   [on Linux](https://wiki.vintagestory.at/Guide:Dedicated_Server#Dedicated_server_on_Linux) or
   [on Windows](https://wiki.vintagestory.at/Guide:Dedicated_Server#Dedicated_server_on_Windows).
   The server is on <https://account.vintagestory.at/downloads> (on Linux, the "Linux tar.gz
   Archive (server only)", `vs_server_linux-x64_<game version>.tar.gz`). On Linux it needs
   .NET 10 ([Installing .NET](https://wiki.vintagestory.at/Installing_the_game_on_Linux#Installing_.NET)),
   and `server.sh` needs `procps` and `screen`. Start it once so it writes its data path (the
   folder that holds `serverconfig.json` and `Saves/`), then stop it.
2. Download `seraphhorizons_<v>_server.zip` from the release and extract it into the server's
   data path. It holds:
   - `Mods/seraphhorizons_<v>.zip`: the pack's own mod;
   - `lock.json`: every other mod, with its download URL (`fileUrl`) and `sha256`;
   - `fetch-mods.sh`: a script that downloads those mods.

   No third-party mod is in the zip: their files are not re-hosted here, so they are fetched from
   the ModDB CDN.
3. Fetch the mods (below).
4. Open port 42420 (TCP and UDP), configure and start the server as the wiki describes
   ([Configuring the server](https://wiki.vintagestory.at/Guide:Dedicated_Server#Configuring_the_server),
   [Opening the firewall](https://wiki.vintagestory.at/Guide:Dedicated_Server#Opening_the_firewall),
   [Running the server](https://wiki.vintagestory.at/Guide:Dedicated_Server#Running_the_server)).

### Fetching the mods on Linux

`fetch-mods.sh` needs `curl` and `sha256sum`. Give it the data path; it writes into that path's
`Mods/`:

```sh
./fetch-mods.sh /path/to/data
```

It skips a file that is already there, checks the sha256 of every file, and stops at the first
one that does not match.

### Fetching the mods on Windows

`fetch-mods.sh` is a POSIX `sh` script. It runs as is under
[Git Bash](https://gitforwindows.org/) or WSL, which have `curl` and `sha256sum`:

```sh
./fetch-mods.sh /c/path/to/data
```

Without either, the same can be done in PowerShell from `lock.json`, run in the data path. Each
entry's `fileUrl` is the download and `sha256` its checksum; the pack's own mod is not in
`lock.json`, since the zip carries it.

```powershell
$ProgressPreference = 'SilentlyContinue'
$lock = Get-Content lock.json -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force Mods | Out-Null
foreach ($m in $lock.mods) {
    $f = Join-Path Mods $m.fileName
    if (-not (Test-Path $f)) { Invoke-WebRequest -Uri $m.fileUrl -OutFile $f }
    if ((Get-FileHash $f -Algorithm SHA256).Hash -ne $m.sha256) { throw "sha256 mismatch: $f" }
}
```

## The world

Start a new world with the pack installed and default world settings. The pack needs no
particular playstyle or world preset.

Some of what the pack changes happens at world generation: ore and panning gravel
([README.md](README.md#ore-and-panning)), ruins, towers and springs, among others. These apply to
newly generated chunks only, so a world created before the pack, or before a release that changed
them, does not have them where it was already explored.

The pack is pre-alpha. No save compatibility is promised between versions.

For admin rights, see
[Acquiring server admin rights](https://wiki.vintagestory.at/Setting_up_a_Multiplayer_Server#Acquiring_server_admin_rights).

## Players

Players need the same mods at the same versions as the server. Point them to the
[Installing](README.md#installing) table in README.md: with the Cairn launcher they open the
release's `seraphhorizons_<v>.cairn`, which installs the right game version and every mod.

## Configuration

Server settings are in `serverconfig.json` in the data path ([Server Config](https://wiki.vintagestory.at/Server_Config)).
Mod settings are in the data path's `ModConfig/`: `~/.cairn/packs/<id>/data/ModConfig/` on the
Cairn route, `<data path>/ModConfig/` on the plain route.

`ModConfig/seraphhorizons.json` holds the pack's own mod's settings, with a switch for each of its
features (all on by default). Two of them concern hosting:

- `FollowPackDefaults`: when a release changes a default, in any mod's config, a setting still at
  the old default moves to the new one on start. A value you set is kept. Each change is logged,
  and admins are told in chat as they join. Set it to `false` to leave every config file alone.
- `PackVersionCheck`: the mod compares the installed mods and game version with the release it came
  with. A missing mod, a mod at another version, a mod not in the pack, or another game version is
  logged as a warning and sent to admins in chat as they join.

How the defaults are tracked: [`docs/config-defaults.md`](docs/config-defaults.md).

## Updating

1. Stop the server and back up `Saves/` and `ModConfig/`.
2. Install the new release:
   - Cairn: install the new release's pack file (`cairn-server install seraphhorizons_<v>.cairn`),
     or, for a pack followed from an address, `cairn-server update seraphhorizons`. Then start the
     server again: `run` installs what the lock says, and a running server keeps its old mods.
   - Plain: empty the data path's `Mods/` (old versions left there would load beside the new
     ones), extract the new release's `seraphhorizons_<v>_server.zip` into the data path, and run
     `fetch-mods.sh` again. If the release changes the game version, update the server first.
3. Start the server and read its log for `Pack version check` warnings: none means the install
   matches the release.

Updating an existing world in place may work, but it is not guaranteed while the pack is
pre-alpha. A fresh world is the safe choice.
