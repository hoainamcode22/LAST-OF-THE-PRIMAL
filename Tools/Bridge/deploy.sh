#!/bin/bash
# deploy.sh <zipname without .zip> : extract Tools/<name>.zip into Assets/_Project (md5 checked), refresh, wait for compile, console check
P="$HOME/mnt/LAST OF THE PRIMAL"; N="$1"; Z="$P/Tools/$N.zip"
[ -f "$Z" ] || { echo "missing $Z"; exit 2; }
python3 - "$Z" "$P/Assets/_Project" <<'PY' || exit 1
import zipfile,json,hashlib,os,sys
z=zipfile.ZipFile(sys.argv[1]); dst=sys.argv[2]
man=json.loads(z.read('_manifest.json')) if '_manifest.json' in z.namelist() else {}
n=0
for rel in z.namelist():
    if rel=='_manifest.json' or rel.endswith('/'): continue
    data=z.read(rel)
    if rel in man and hashlib.md5(data).hexdigest()!=man[rel]: print("MD5 MISMATCH",rel); sys.exit(1)
    out=os.path.join(dst,rel); os.makedirs(os.path.dirname(out),exist_ok=True); open(out,'wb').write(data); n+=1
    print("wrote",rel)
print(n,"files extracted")
PY
T=$(date +%H%M%S)
"$(dirname "$0")/run.sh" "D_${N}_r$T" PrimalEditorBridge.Refresh "" 5 >/dev/null
"$(dirname "$0")/run.sh" "D_${N}_p$T" PrimalEditorBridge.Ping "" 10 | grep -E '"(ok|result|error)"'
"$(dirname "$0")/run.sh" "D_${N}_c$T" PrimalEditorBridge.ConsoleCheck "" 5 | grep -E '"(result|error)"'
