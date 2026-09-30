#!/bin/bash
# run.sh <id> <Type.Method> [arg] [wait_minutes=5] : send one command to PrimalEditorBridge (Unity editor must be open)
# ids must be fresh and prefixed per agent; methods need Type.Method, e.g. PrimalEditorBridge.Ping
B="$HOME/mnt/LAST OF THE PRIMAL/Library/PrimalBridge"; ID="$1"; M="$2"; ARG="${3:-}"; W="${4:-5}"
[ -z "$ID" ] || [ -z "$M" ] && { echo "usage: run.sh <id> <Type.Method> [arg] [wait_min]"; exit 2; }
mkdir -p "$B"; R="$B/result_$ID.json"
if [ -f "$R" ]; then echo "result_$ID.json already exists (use a fresh id):"; cat "$R"; exit 0; fi
python3 - "$B/command.json" "$ID" "$M" "$ARG" <<'PY'
import json,sys; open(sys.argv[1],'w').write(json.dumps({"id":sys.argv[2],"method":sys.argv[3],"arg":sys.argv[4]}))
PY
end=$(( $(date +%s) + W*60 ))
while [ $(date +%s) -lt $end ]; do [ -f "$R" ] && { sleep 1; cat "$R"; exit 0; }; sleep 2; done
echo "TIMEOUT after ${W} min: no result_$ID.json (editor closed, compiling, in Play mode, or busy)"; exit 1
