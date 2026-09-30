import bpy, sys, json
def dump(path):
    bpy.ops.wm.open_mainfile(filepath=path)
    out = {}
    for a in bpy.data.actions:
        fcs = []
        for sl in a.layers:
            for st in sl.strips:
                for cb in st.channelbags:
                    for fc in cb.fcurves:
                        v = [0.0] * (len(fc.keyframe_points) * 2); fc.keyframe_points.foreach_get("co", v)
                        fcs.append((fc.data_path, fc.array_index, v))
        out[a.name] = dict(fr=list(a.frame_range), fcs=sorted(fcs, key=lambda x: (x[0], x[1])), meta=a.get("pf_meta"), loop=bool(a.get("pf_loop", False)))
    return out
A = dump(sys.argv[-2]); B = dump(sys.argv[-1])
res = {"only_old": sorted(set(A) - set(B)), "only_new": sorted(set(B) - set(A)), "changed": {}, "identical": 0}
def cmp(a, b):
    if a["fr"] != b["fr"] or len(a["fcs"]) != len(b["fcs"]): return "shape"
    m = 0.0
    for x, y in zip(a["fcs"], b["fcs"]):
        if x[0] != y[0] or x[1] != y[1] or len(x[2]) != len(y[2]): return "curves"
        m = max([m] + [abs(p - q) for p, q in zip(x[2], y[2])])
    return m
for n in sorted(set(A) & set(B)):
    d = cmp(A[n], B[n]); 
    if d == 0.0 and A[n]["meta"] == B[n]["meta"]: res["identical"] += 1
    else: res["changed"][n] = d
res["straight_vs_old_heavy"] = cmp(A["BareHand_Heavy"], B["BareHand_Heavy_Straight"])
res["n_fcurves_heavy_straight"] = len(B["BareHand_Heavy_Straight"]["fcs"])
print("JSON", json.dumps(res))
