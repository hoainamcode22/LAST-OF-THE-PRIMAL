#!/bin/bash
# P_ Phase 2 deploy: copy staged files into Assets/_Project/Scripts (backup first, refuse if a target changed since staging)
P="$HOME/mnt/LAST OF THE PRIMAL"; S="$P/Tools/_P2_stage"; D="$P/Assets/_Project/Scripts"; B="$P/Tools/_backups_P2/Assets/_Project/Scripts"
cd "$S" || exit 2
bad=0
while read -r sum rel; do
  cur=$(md5sum "$D/$rel" | awk '{print $1}')
  if [ "$cur" != "$sum" ] && [ "$cur" != "$(md5sum "$S/$rel" | awk '{print $1}')" ]; then echo "CHANGED since staging: $rel ($cur)"; bad=1; fi
done < _orig_md5.txt
[ "$bad" = 1 ] && [ "$1" != "force" ] && { echo "abort"; exit 1; }
while read -r sum rel; do
  mkdir -p "$(dirname "$B/$rel")"; [ -f "$B/$rel" ] || cp -p "$D/$rel" "$B/$rel"
done < _orig_md5.txt
n=0
for f in $(find . -name "*.cs" | sed 's|^\./||'); do mkdir -p "$(dirname "$D/$f")"; cp "$S/$f" "$D/$f"; n=$((n+1)); echo "wrote $f $(md5sum "$D/$f" | awk '{print $1}')"; done
echo "$n files"
