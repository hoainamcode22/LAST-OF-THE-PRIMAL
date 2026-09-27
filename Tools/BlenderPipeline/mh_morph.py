"""Build the PRIMAL survivor body from the CC0 MakeHuman base mesh + targets (numpy only, runs outside Blender).
usage: python3 mh_morph.py <mh_data_dir> <params.json> <out_prefix>
writes <out>.obj (body + eye/teeth helpers, UVs, groups, Blender space: metres, Z up, facing -Y, soles at z=0)
and <out>_joints.json (joint helper centroids in the same space).
Macro weighting follows MakeHuman 1.1 (apps/human.py): each macro target weight = product of the values of the
variables named in its file name."""
import sys, os, json, re, numpy as np

def read_obj(path):
    V=[]; VT=[]; F=[]; g=None
    for ln in open(path):
        if ln.startswith('v '): V.append([float(x) for x in ln.split()[1:4]])
        elif ln.startswith('vt '): VT.append([float(x) for x in ln.split()[1:3]])
        elif ln.startswith('g '): g=ln.split()[1]
        elif ln.startswith('f '):
            idx=[tuple(int(p) - 1 if p else -1 for p in (t.split('/') + [''])[:2]) for t in ln.split()[1:]]
            F.append((g, idx))
    return np.array(V), np.array(VT), F

def read_target(path):
    idx=[]; d=[]
    for ln in open(path):
        if not ln.strip() or ln.startswith('#'): continue
        p=ln.split(); idx.append(int(p[0])); d.append([float(p[1]),float(p[2]),float(p[3])])
    return np.array(idx,dtype=int), np.array(d).reshape(-1,3)

def macro_vals(P):
    v={}
    g=P.get('gender',1.0); v['male']=g; v['female']=1-g
    a=P.get('age',0.5)
    if a<0.5:
        v['old']=0; v['baby']=max(0,1-a*5.333); v['young']=max(0,(a-0.1875)*3.2); v['child']=max(0,min(1,5.333*a)-v['young'])
    else:
        v['child']=v['baby']=0; v['old']=max(0,a*2-1); v['young']=1-v['old']
    m=P.get('muscle',0.5); v['maxmuscle']=max(0,m*2-1); v['minmuscle']=max(0,1-m*2); v['averagemuscle']=1-v['maxmuscle']-v['minmuscle']
    w=P.get('weight',0.5); v['maxweight']=max(0,w*2-1); v['minweight']=max(0,1-w*2); v['averageweight']=1-v['maxweight']-v['minweight']
    h=P.get('height',0.5); v['maxheight']=max(0,h*2-1); v['minheight']=max(0,1-h*2)
    b=P.get('proportions',0.5); v['idealproportions']=max(0,b*2-1); v['uncommonproportions']=max(0,1-b*2)
    r=P.get('race',{'african':1/3,'asian':1/3,'caucasian':1/3}); s=sum(r.values())
    for k in ('african','asian','caucasian'): v[k]=r.get(k,0)/s
    return v

def main(data, params, out):
    P=json.load(open(params))
    V,VT,F=read_obj(os.path.join(data,'3dobjs','base.obj'))
    X=V.copy(); T=os.path.join(data,'targets')
    vals=macro_vals(P); applied=[]
    def add(rel, w):
        if abs(w)<1e-6: return
        i,d=read_target(os.path.join(T,rel)); X[i]+=w*d; applied.append((rel,round(w,4)))
    for sub in ('macrodetails','macrodetails/height','macrodetails/proportions'):
        for fn in sorted(os.listdir(os.path.join(T,sub))):
            if not fn.endswith('.target'): continue
            toks=fn[:-7].split('-')
            if toks[0]=='universal': toks=toks[1:]
            w=1.0
            for t in toks:
                if t not in vals: w=None; break
                w*=vals[t]
            if w: add(f'{sub}/{fn}', w)
    # detail modifiers: {"folder/name": value}; value>0 -> name-incr/..., value<0 -> name-decr (or explicit pair "a|b")
    for key,val in P.get('modifiers',{}).items():
        folder,name=key.split('/')
        if '|' in name:
            lo,hi=name.split('|'); add(f'{folder}/{hi if val>0 else lo}.target', abs(val)); continue
        pairs=[('incr','decr'),('up','down'),('forward','backward'),('out','in'),('convex','concave'),('compress','uncompress')]
        done=False
        for pos,neg in pairs:
            fp=os.path.join(T,folder,f'{name}-{pos}.target'); fn_=os.path.join(T,folder,f'{name}-{neg}.target')
            if os.path.exists(fp) or os.path.exists(fn_):
                add(f'{folder}/{name}-{pos if val>0 else neg}.target', abs(val)); done=True; break
        if not done: add(f'{folder}/{name}.target', val)
    # facial expression units (race weighted, MakeHuman expression/units/<race>/<unit>.target)
    def unit_delta(unit):
        d = np.zeros_like(X)
        for race in ('african', 'asian', 'caucasian'):
            f = os.path.join(T, 'expression', 'units', race, unit + '.target')
            if vals[race] > 0 and os.path.exists(f):
                i, dd = read_target(f); d[i] += vals[race] * dd
        return d
    for unit, w in P.get('expression', {}).items():
        X += w * unit_delta(unit); applied.append(('expression/' + unit, w))
    shapes = {}
    for name, units in P.get('shapes', {}).items():
        d = np.zeros_like(X)
        for unit, w in units.items(): d += w * unit_delta(unit)
        shapes[name] = np.stack([d[:, 0], -d[:, 2], d[:, 1]], 1) * 0.1
    # MakeHuman: Y up, faces +Z, 1 unit = 1 dm -> Blender: metres, Z up, faces -Y
    B=np.stack([X[:,0], -X[:,2], X[:,1]],1)*0.1
    keep=lambda g: g=='body' or g in ('helper-l-eye','helper-r-eye','helper-upper-teeth','helper-lower-teeth','helper-tongue')
    body_v=sorted({i for g,idx in F if g=='body' for i,_ in idx})
    B[:,2]-=B[body_v,2].min()
    # joint centroids
    J={}
    for g,idx in F:
        if g and g.startswith('joint-'):
            J.setdefault(g,set()).update(i for i,_ in idx)
    J={k[6:]:B[sorted(s)].mean(0).round(5).tolist() for k,s in J.items()}
    # eye / teeth helper centres
    for g in ('helper-l-eye','helper-r-eye','helper-upper-teeth','helper-lower-teeth'):
        s=sorted({i for gg,idx in F if gg==g for i,_ in idx}); pts=B[s]
        J[g]={'center':pts.mean(0).round(5).tolist(),'min':pts.min(0).round(5).tolist(),'max':pts.max(0).round(5).tolist()}
    # write obj (only kept groups, reindexed)
    used=sorted({i for g,idx in F if keep(g) for i,_ in idx}); remap={o:n for n,o in enumerate(used)}
    usedt=sorted({t for g,idx in F if keep(g) for _,t in idx if t>=0}); remapt={o:n for n,o in enumerate(usedt)}
    with open(out+'.obj','w') as f:
        f.write('# PRIMAL survivor body, built from the MakeHuman CC0 base mesh + CC0 targets\n')
        for i in used: f.write('v %.6f %.6f %.6f\n'%tuple(B[i]))
        for t in usedt: f.write('vt %.6f %.6f\n'%tuple(VT[t]))
        cur=None
        for g,idx in F:
            if not keep(g): continue
            if g!=cur: f.write(f'g {g}\n'); cur=g
            f.write('f '+' '.join(f'{remap[i]+1}/{remapt[t]+1}' for i,t in idx)+'\n')
    if shapes:
        np.savez(out + '_shapes.npz', **{k: v[used].astype(np.float32) for k, v in shapes.items()})
    vgroups={}
    for g,idx in F:
        if keep(g): vgroups.setdefault(g,set()).update(remap[i] for i,_ in idx)
    json.dump({k:sorted(v) for k,v in vgroups.items()},open(out+'_groups.json','w'))
    json.dump({'joints':J,'applied':applied,'params':P,'height_m':float(B[body_v,2].max())},open(out+'_joints.json','w'),indent=1)
    print('verts',len(used),'height',round(float(B[body_v,2].max()),3),'targets',len(applied))

if __name__=='__main__': main(*sys.argv[1:4])
