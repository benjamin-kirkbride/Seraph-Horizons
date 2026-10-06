#!/usr/bin/env bash
# Run the ore survey for several seeds in parallel; README.md has the whole procedure.
#
# usage: tools/ore-survey/run.sh [options] SEED...
#   --size N         chunk columns per side (default 160; a multiple of 8)
#   --vanilla        no mods but the survey's own (default: every mod in build/mods)
#   --mods DIR       take the pack's mods from DIR instead of build/mods
#   --mod ZIP        add a mod zip, replacing any staged zip of the same modid (repeatable),
#                    e.g. a local build of seraphhorizons with a worldgen change
#   --modconfig DIR  copy DIR in as the server's ModConfig (worldgen config experiments)
#   --name NAME      setup name in the output folder (default: pack or vanilla)
#   --dump           write the deposit/drops/metal-units dump instead of scanning; one run,
#                    seeds are ignored
#   -j N             runs at once (default 4)
#   --port N         first port (default 42460); run k uses N+k
#
# Each run gets build/ore-survey/<name>-s<seed>/ (or <name>-dump/) as the server's data
# path, holding orescan.json, orescan.json.cells.csv, orescan.json.log and server.out.
# The world, Mods and Cache are deleted afterwards. Needs VINTAGE_STORY and a Release
# build of the mod (build/seraphoresurvey.zip).
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
size=160 vanilla=0 mods_dir=$root/build/mods modconfig="" name="" dump=0 jobs=4 port=42460
extra=()
seeds=()
while [ $# -gt 0 ]; do
  case $1 in
    --size) size=$2; shift 2 ;;
    --vanilla) vanilla=1; shift ;;
    --mods) mods_dir=$2; shift 2 ;;
    --mod) extra+=("$(readlink -f "$2")"); shift 2 ;;
    --modconfig) modconfig=$(readlink -f "$2"); shift 2 ;;
    --name) name=$2; shift 2 ;;
    --dump) dump=1; shift ;;
    -j) jobs=$2; shift 2 ;;
    --port) port=$2; shift 2 ;;
    -h|--help) sed -n '2,23p' "$0"; exit 0 ;;
    -*) echo "unknown option $1" >&2; exit 2 ;;
    *) seeds+=("$1"); shift ;;
  esac
done
: "${VINTAGE_STORY:?set VINTAGE_STORY to a 1.22.7 server or client install}"
zip=$root/build/seraphoresurvey.zip
[ -f "$zip" ] || { echo "no $zip: run dotnet build tools/ore-survey -c Release first" >&2; exit 1; }
[ -n "$name" ] || { [ $vanilla = 1 ] && name=vanilla || name=pack; }
if [ $dump = 1 ]; then seeds=(dump); fi
[ ${#seeds[@]} -gt 0 ] || { echo "no seeds given" >&2; exit 2; }

modid_of() {  # modid in a mod zip's modinfo.json (keys are case-insensitive in the game)
  unzip -p "$1" modinfo.json 2>/dev/null | python3 -c '
import re, sys
m = re.search(r"\"modid\"\s*:\s*\"([^\"]+)\"", sys.stdin.read(), re.I)
print(m.group(1).lower() if m else "")'
}

survey() {  # survey SEED PORT
  local seed=$1 p=$2 data
  if [ "$seed" = dump ]; then data=$root/build/ore-survey/$name-dump; else data=$root/build/ore-survey/$name-s$seed; fi
  rm -rf "$data"; mkdir -p "$data/Mods"
  cp "$zip" "$data/Mods/"
  if [ $vanilla = 0 ]; then
    cp -r "$mods_dir"/. "$data/Mods/"
  fi
  local z id f
  for z in "${extra[@]}"; do
    id=$(modid_of "$z")
    for f in "$data"/Mods/*.zip; do
      if [ -n "$id" ] && [ "$(basename "$f")" != seraphoresurvey.zip ] && [ "$(modid_of "$f")" = "$id" ]; then
        echo "seed $seed: $(basename "$z") replaces $(basename "$f")"
        rm -f "$f"
      fi
    done
    cp "$z" "$data/Mods/"
  done
  if [ -n "$modconfig" ]; then cp -r "$modconfig" "$data/ModConfig"; fi
  echo "seed $seed: $data (port $p)"
  # The world config goes on the command line: a partial serverconfig.json lacks the default
  # player groups and the server refuses to start (as in packtool smoke).
  (
    cd "$VINTAGE_STORY"
    ORE_SURVEY_OUT=$data/orescan.json ORE_SURVEY_SIZE=$size \
    ORE_SURVEY_MODE=$([ "$seed" = dump ] && echo dump || echo scan) \
    dotnet VintagestoryServer.dll --dataPath "$data" --port "$p" \
      '--withconfig={"WorldConfig":{"Seed":"'"$([ "$seed" = dump ] && echo 101 || echo "$seed")"'","WorldName":"oresurvey","WorldType":"standard","PlayStyle":"surviveandbuild"}}' \
      > "$data/server.out" 2>&1 < /dev/null
  ) || echo "seed $seed: server exited with $?" >&2
  rm -rf "$data/Saves" "$data/Mods" "$data/Cache"
  if [ "$seed" = dump ]; then [ -s "$data/orescan.json" ]; else [ -s "$data/orescan.json.cells.csv" ]; fi \
    && echo "seed $seed: done, $(tail -n 1 "$data/orescan.json.log")" \
    || { echo "seed $seed: no output, see $data/server.out" >&2; return 1; }
}

mkdir -p "$root/build/ore-survey"
# Failures are counted in a file: a job's status is lost once `wait -n` has reaped it.
failed=$(mktemp)
trap 'rm -f "$failed"' EXIT
k=0
for seed in "${seeds[@]}"; do
  while [ "$(jobs -rp | wc -l)" -ge "$jobs" ]; do wait -n || true; done
  { survey "$seed" $((port + k)) || echo "$seed" >> "$failed"; } &
  k=$((k + 1))
done
wait
if [ -s "$failed" ]; then echo "failed: $(tr '\n' ' ' < "$failed")" >&2; exit 1; fi
