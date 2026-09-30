import json, math
N = json.load(open("fbxdump_new.json")); O = json.load(open("fbxdump_old.json"))
man = json.load(open("staging_new3/clips_manifest.json")); mo = json.load(open("staging_old/clips_manifest.json"))
M = {c["name"]: c for c in man["clips"]}; MO = {c["name"]: c for c in mo["clips"]}
out = {}
out["fbx_new_clips"] = len(N["clips"]); out["manifest_clips"] = len(M); out["fbx_old_clips"] = len(O["clips"])
out["names_match"] = sorted(N["clips"]) == sorted(M)
out["frame_mismatch"] = [n for n in M if N["clips"][n]["frames"] != M[n]["frames"]]
out["events_outside"] = [(n, e) for n, c in M.items() for e in c["events"] if not (0 <= e["frame"] <= c["frames"])]
out["manifest_changed_vs_old"] = sorted(n for n in MO if n in M and json.dumps(MO[n], sort_keys=True) != json.dumps(M[n], sort_keys=True))
out["manifest_added"] = sorted(set(M) - set(MO))
out["armature"] = N["armature"]; out["bones"] = N["bones"]; out["meshes"] = N["meshes"]
def qang(a, b):
    d = abs(sum(x * y for x, y in zip(a, b))); return math.degrees(2 * math.acos(min(1.0, d)))
# unchanged clips: new FBX vs old FBX
worst = (0, None)
for n in O["clips"]:
    if n in ("BareHand_Heavy", "BareHand_Block", "Unarmed_Block"): continue
    a, b = O["clips"][n]["s"], N["clips"][n]["s"]
    for f in a:
        if f not in b: continue
        for bn, q in a[f]["q"].items():
            d = qang(q, b[f]["q"][bn])
            if d > worst[0]: worst = (d, (n, f, bn))
        dl = max(abs(x - y) for x, y in zip(a[f]["loc"], b[f]["loc"]))
        if dl * 100 > worst[0]: pass
out["unchanged_clips_max_rot_diff_deg"] = [round(worst[0], 4), worst[1]]
# heavy straight (new) vs heavy (old)
a, b = O["clips"]["BareHand_Heavy"]["s"], N["clips"]["BareHand_Heavy_Straight"]["s"]
out["straight_vs_old_heavy_max_deg"] = round(max(qang(q, b[f]["q"][bn]) for f in a for bn, q in a[f]["q"].items()), 4)
def sub(p, q): return [x - y for x, y in zip(p, q)]
def norm(v): return math.sqrt(sum(x * x for x in v))
rep = {}
for n in ("BareHand_Punch_1", "BareHand_Punch_2", "BareHand_Punch_3", "BareHand_Heavy", "BareHand_Combo_End", "Kick", "Unarmed_Block",
          "Gather_Branch", "Gather_Stone_Hand", "Drink_Kneel", "Collect_Water", "Bandage_Use", "Unconscious", "Unconscious_Collapse"):
    c = N["clips"][n]; S = c["s"]; fr = sorted(S, key=int); F = len(fr)
    r = {"frames": c["frames"], "loop": M[n]["loop"], "events": [(e["frame"], e["function"], e["param"]) for e in M[n]["events"]]}
    f0 = fr[0]
    def rng(bn): return round(max(qang(S[f0]["q"][bn], S[f]["q"][bn]) for f in fr), 1)
    r["rot_range_deg"] = {bn: rng(bn) for bn in ("Pelvis", "Spine", "Chest", "UpperArm_L", "UpperArm_R", "LowerArm_L", "LowerArm_R", "Hand_L", "Hand_R", "Thigh_R", "Calf_R")}
    pz = [S[f]["w"]["Pelvis"] for f in fr]
    r["pelvis_travel_cm"] = [round((max(p[i] for p in pz) - min(p[i] for p in pz)) * 100, 1) for i in range(3)]
    feet = {}
    for sd in "LR":
        ball = [S[f]["w"][f"Toe_{sd}"] for f in fr]; tip = [S[f]["wt"][f"Toe_{sd}"] for f in fr]
        planted = [ball[i][2] < 0.035 and tip[i][2] < 0.03 for i in range(F)]
        sl = []
        for i in range(1, F):
            if planted[i] and planted[i - 1]:
                sl.append(max(norm(sub(ball[i][:2], ball[i - 1][:2])), norm(sub(tip[i][:2], tip[i - 1][:2]))))
        feet[sd] = {"planted_frames": sum(planted), "max_slide_mm_per_frame": round(max(sl) * 1000, 1) if sl else None,
                    "mean_slide_mm_per_frame": round(sum(sl) / len(sl) * 1000, 2) if sl else None, "min_z_mm": round(min(min(b[2], t[2]) for b, t in zip(ball, tip)) * 1000, 1)}
    r["feet"] = feet
    if M[n]["loop"]:
        a, b = S[fr[0]], S[fr[-1]]
        r["loop_seam_deg"] = round(max(qang(a["q"][bn], b["q"][bn]) for bn in a["q"]), 3)
    rep[n] = r
out["clips"] = rep
# Unconscious f0 vs Wake_Up f0, collapse end vs Unconscious f0
w0 = N["clips"]["Wake_Up"]["s"]["0"]; u0 = N["clips"]["Unconscious"]["s"]["0"]; ce = N["clips"]["Unconscious_Collapse"]["s"][str(N["clips"]["Unconscious_Collapse"]["frames"])]
out["uncon0_vs_wakeup0_deg"] = round(max(qang(u0["q"][b], w0["q"][b]) for b in u0["q"]), 4)
out["uncon0_vs_wakeup0_pelvis_mm"] = round(norm(sub(u0["w"]["Pelvis"], w0["w"]["Pelvis"])) * 1000, 2)
out["collapse_end_vs_uncon0_deg"] = round(max(qang(u0["q"][b], ce["q"][b]) for b in u0["q"]), 4)
out["collapse_end_vs_uncon0_pelvis_mm"] = round(norm(sub(u0["w"]["Pelvis"], ce["w"]["Pelvis"])) * 1000, 2)
out["pelvis_z_rest_check"] = N["clips"]["Idle"]["s"]["0"]["w"]["Pelvis"]
json.dump(out, open("verify.json", "w"), indent=1)
print(json.dumps({k: v for k, v in out.items() if k != "clips"}, indent=0))
for n, r in rep.items(): print(n, r["frames"], r["rot_range_deg"], r["pelvis_travel_cm"], r["feet"], r.get("loop_seam_deg"))
