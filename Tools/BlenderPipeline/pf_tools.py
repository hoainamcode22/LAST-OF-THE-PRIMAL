"""Primitive tool / weapon / item / camp props for PRIMAL FRONTIER (original, procedural). One shared material M_Tools
(atlas T_Tools, see tex_tools.py). Convention: grip at the origin, handle along +Y, working end up (+Y), blade edge / point +Z."""
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, noise

TEX = r"E:\Model game khủng long\textures"
R = dict(wood=(0, .5, .5, 1), stone=(.5, .5, 1, 1), bone=(0, .25, .25, .5), horn=(.25, .25, .5, .5), leather=(.5, .25, .75, .5),
         cord=(.75, .25, 1, .5), thatch=(0, 0, .5, .25), gourd=(.5, 0, .625, .25), berries=(.625, 0, .75, .25), meat_raw=(.75, 0, .875, .25),
         meat_cooked=(.875, 0, 1, .25))

def material():
    m = bpy.data.materials.get("M_Tools")
    if m: return m
    m = bpy.data.materials.new("M_Tools"); m.use_nodes = True; nt = m.node_tree; bs = nt.nodes["Principled BSDF"]
    def img(n, nc=False):
        i = bpy.data.images.get(n) or bpy.data.images.load(f"{TEX}\\{n}"); i.colorspace_settings.name = "Non-Color" if nc else "sRGB"; return i
    d = nt.nodes.new("ShaderNodeTexImage"); d.image = img("T_Tools_D.png"); nt.links.new(d.outputs[0], bs.inputs["Base Color"])
    n = nt.nodes.new("ShaderNodeTexImage"); n.image = img("T_Tools_N.png", True); nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(n.outputs[0], nm.inputs["Color"]); nt.links.new(nm.outputs[0], bs.inputs["Normal"])
    bs.inputs["Roughness"].default_value = 0.7
    return m

class Builder:
    """accumulates parts (each with its own UV region) into one bmesh"""
    def __init__(self, seed=1):
        self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.verify(); random.seed(seed); self.seed = seed

    def _map(self, faces, reg, fn):
        u0, v0, u1, v1 = R[reg]
        for f in faces:
            for l in f.loops:
                u, v = fn(l.vert.co)
                l[self.uv].uv = (u0 + (u1 - u0) * (u % 1.0) * 0.98 + 0.01 * (u1 - u0), v0 + (v1 - v0) * (v % 1.0) * 0.98 + 0.01 * (v1 - v0))

    def cyl(self, reg, p0, p1, r0, r1, segs=8, rings=4, jitter=0.0, bend=None, cap=True, twist_uv=1.0):
        p0 = Vector(p0); p1 = Vector(p1); ax = (p1 - p0); L = ax.length; ax.normalize()
        up = Vector((1, 0, 0)) if abs(ax.x) < 0.9 else Vector((0, 1, 0)); xa = ax.cross(up).normalized(); ya = ax.cross(xa)
        rows = []
        for i in range(rings + 1):
            t = i / rings; c = p0 + ax * L * t
            if bend: c = c + Vector(bend) * math.sin(math.pi * t)
            r = r0 + (r1 - r0) * t
            row = []
            for k in range(segs):
                a = 2 * math.pi * k / segs; rr = r * (1 + jitter * (random.random() - 0.5))
                row.append(self.bm.verts.new(c + (xa * math.cos(a) + ya * math.sin(a)) * rr))
            rows.append(row)
        faces = []
        for i in range(rings):
            for k in range(segs):
                f = self.bm.faces.new((rows[i][k], rows[i][(k + 1) % segs], rows[i + 1][(k + 1) % segs], rows[i + 1][k])); faces.append((f, i, k))
        for f, i, k in faces:
            for l in f.loops:
                ri = i + (1 if l.vert in rows[i + 1] else 0); kk = k + (1 if (l.vert is rows[i][(k + 1) % segs] or l.vert is rows[min(i + 1, rings)][(k + 1) % segs]) else 0)
                u0, v0, u1, v1 = R[reg]
                l[self.uv].uv = (u0 + (u1 - u0) * (0.02 + 0.96 * kk / segs), v0 + (v1 - v0) * (0.02 + 0.96 * ((ri / rings) * L * twist_uv / 0.6) % 1.0))
        if cap:
            for row in (rows[0], rows[-1]):
                f = self.bm.faces.new(row if row is rows[-1] else list(reversed(row)))
                self._map([f], reg, lambda co: (co.x * 8, co.z * 8))
        return rows

    def blob(self, reg, center, size, seed=0, rough=0.25, subdiv=2, flat=None, facet=True):
        g = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=1.0)
        vs = g["verts"]
        for v in vs:
            n = v.co.normalized(); d = 1 + rough * noise.noise(n * 2.2 + Vector((seed, seed * 0.3, 0))) + rough * 0.4 * noise.noise(n * 5 + Vector((0, seed, 1)))
            if facet: d *= 1 - 0.08 * abs(math.sin(n.x * 9 + seed) * math.cos(n.z * 7))
            v.co = Vector((n.x * size[0], n.y * size[1], n.z * size[2])) * d + Vector(center)
        faces = list({f for v in vs for f in v.link_faces})
        c = Vector(center)
        self._map(faces, reg, lambda co: ((co - c).x * 6 + (co - c).y * 3, (co - c).z * 6 + (co - c).y * 2))
        return vs

    def blade(self, reg, base, length, width, thick, point=0.0, flat_axis='X', n_len=7, n_w=5, seed=0, serr=0.0):
        """knapped leaf/wedge blade in the local YZ plane (length along +Y, edge towards +Z), lens-shaped thickness along X"""
        base = Vector(base); verts = {}
        for i in range(n_len + 1):
            t = i / n_len
            w = width * (math.sin(math.pi * min(1.0, t * (1 - point * 0.5) + 0.05)) ** (0.6 + point)) if point else width * (0.55 + 0.45 * t)
            if point: w *= (1 - t) ** 0.6 + 0.02
            for j in range(n_w + 1):
                s = j / n_w * 2 - 1
                y = length * t; z = w * s * 0.5
                if serr and abs(s) > 0.99: z += serr * math.sin(t * 40 + seed)
                th = thick * (1 - s * s) ** 0.7 * (1 - 0.7 * t if point else 1 - 0.3 * t)
                jit = 0.004 * (noise.noise(Vector((t * 9, s * 7, seed))) )
                verts[(i, j, 0)] = self.bm.verts.new(base + Vector((th * 0.5 + jit, y, z)))
                verts[(i, j, 1)] = self.bm.verts.new(base + Vector((-th * 0.5 - jit, y, z)))
        faces = []
        for i in range(n_len):
            for j in range(n_w):
                faces.append(self.bm.faces.new((verts[(i, j, 0)], verts[(i + 1, j, 0)], verts[(i + 1, j + 1, 0)], verts[(i, j + 1, 0)])))
                faces.append(self.bm.faces.new((verts[(i, j + 1, 1)], verts[(i + 1, j + 1, 1)], verts[(i + 1, j, 1)], verts[(i, j, 1)])))
        for i in range(n_len):
            for j in (0, n_w):
                a, b = (verts[(i, j, 0)], verts[(i + 1, j, 0)]), (verts[(i, j, 1)], verts[(i + 1, j, 1)])
                fs = (a[0], b[0], b[1], a[1]) if j == 0 else (a[0], a[1], b[1], b[0])
                faces.append(self.bm.faces.new(fs))
        for j in range(n_w):
            faces.append(self.bm.faces.new((verts[(0, j, 1)], verts[(0, j, 0)], verts[(0, j + 1, 0)], verts[(0, j + 1, 1)])))
            faces.append(self.bm.faces.new((verts[(n_len, j, 0)], verts[(n_len, j, 1)], verts[(n_len, j + 1, 1)], verts[(n_len, j + 1, 0)])))
        self._map(faces, reg, lambda co: ((co - base).z * 5 + 0.5, (co - base).y * 5))
        return faces

    def wrap(self, reg, center, axis, radius, length, turns=5, thick=0.004):
        """binding: stacked slightly tilted rings"""
        axis = Vector(axis).normalized(); c0 = Vector(center) - axis * length / 2
        for i in range(turns):
            c = c0 + axis * length * (i + 0.5) / turns
            self.cyl(reg, c - axis * thick, c + axis * thick, radius + thick, radius + thick, segs=8, rings=1, cap=False, twist_uv=3)

    def build(self, name, collection=None):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])
        old = bpy.data.objects.get(name)
        if old: bpy.data.objects.remove(old, do_unlink=True)
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me); self.bm.free()
        o = bpy.data.objects.new(name, me); (collection or bpy.context.scene.collection).objects.link(o)
        me.materials.append(material())
        for p in me.polygons: p.use_smooth = True
        try: me.set_sharp_from_angle(angle=math.radians(40))
        except Exception: pass
        return o

# ------------------------------------------------------------------ items
def hand_stone(col):
    b = Builder(3); b.blob("stone", (0, 0.03, 0), (0.045, 0.06, 0.035), seed=3, rough=0.2, subdiv=2)
    return b.build("TOOL_HandStone", col)

def flint_knife(col):
    b = Builder(4)
    b.cyl("bone", (0, -0.06, 0), (0, 0.07, 0), 0.016, 0.013, segs=8, rings=4, jitter=0.1)
    b.blob("bone", (0, -0.065, 0), (0.02, 0.014, 0.02), seed=4, rough=0.15, subdiv=1)
    b.blade("stone", (0, 0.065, 0), 0.13, 0.042, 0.012, point=1.0, seed=4, serr=0.001)
    b.wrap("cord", (0, 0.07, 0), (0, 1, 0), 0.016, 0.03, turns=5)
    return b.build("TOOL_FlintKnife", col)

def stone_hammer(col):
    b = Builder(5)
    b.cyl("wood", (0, -0.12, 0), (0, 0.24, 0), 0.016, 0.014, segs=8, rings=5, jitter=0.08)
    b.blob("stone", (0, 0.26, 0.0), (0.055, 0.045, 0.06), seed=5, rough=0.18, subdiv=2)
    b.wrap("leather", (0, 0.24, 0), (0, 1, 0), 0.02, 0.06, turns=4, thick=0.006)
    return b.build("TOOL_StoneHammer", col)

def stone_axe(col):
    b = Builder(6)
    b.cyl("wood", (0, -0.14, 0), (0, 0.36, 0), 0.017, 0.015, segs=8, rings=6, jitter=0.08, bend=(0, 0, 0.01))
    vs = b.blob("stone", (0, 0.30, 0.035), (0.02, 0.04, 0.075), seed=6, rough=0.12, subdiv=2)   # head across the haft, edge towards +Z
    for v in vs:                                                                              # grind a cutting edge
        k = max(0.0, (v.co.z - 0.035) / 0.075)
        v.co.x *= 1.0 - 0.75 * k * k
        if v.co.z < 0.0: v.co.z = 0.0 + (v.co.z - 0.0) * 0.4                                  # butt end stays close to the haft
    b.wrap("cord", (0, 0.30, 0), (0, 1, 0), 0.02, 0.08, turns=6)
    b.cyl("cord", (0, 0.27, 0.0), (0, 0.33, 0.035), 0.005, 0.005, segs=4, rings=1, cap=False)   # diagonal lashing
    b.cyl("cord", (0, 0.33, 0.0), (0, 0.27, 0.035), 0.005, 0.005, segs=4, rings=1, cap=False)
    return b.build("TOOL_StoneAxe", col)

def stone_pick(col):
    b = Builder(7)
    b.cyl("wood", (0, -0.14, 0), (0, 0.38, 0), 0.017, 0.015, segs=8, rings=6, jitter=0.08)
    b.cyl("stone", (0, 0.33, -0.12), (0, 0.33, 0.13), 0.02, 0.006, segs=6, rings=4, jitter=0.25)   # double pointed head across the haft
    b.wrap("leather", (0, 0.33, 0), (0, 1, 0), 0.021, 0.06, turns=5, thick=0.005)
    return b.build("TOOL_StonePick", col)

def spear(col):
    b = Builder(8)
    b.cyl("wood", (0, -0.9, 0), (0, 0.95, 0), 0.015, 0.012, segs=8, rings=10, jitter=0.05, bend=(0.012, 0, 0))
    b.blade("stone", (0, 0.93, 0), 0.16, 0.05, 0.012, point=1.0, seed=8, serr=0.0015)
    b.wrap("cord", (0, 0.95, 0), (0, 1, 0), 0.014, 0.05, turns=7)
    b.wrap("leather", (0, 0.0, 0), (0, 1, 0), 0.016, 0.14, turns=5, thick=0.004)       # grip
    return b.build("WEAPON_StoneSpear", col)

def bow(col):
    b = Builder(9); pts = []
    for i in range(13):
        t = i / 12 * 2 - 1; y = t * 0.62; z = -0.09 * (1 - t * t) - 0.02 * (1 - abs(t))
        pts.append(Vector((0, y, z)))
    for i in range(12):
        r0 = 0.016 * (1 - 0.55 * abs(i / 12 * 2 - 1)); r1 = 0.016 * (1 - 0.55 * abs((i + 1) / 12 * 2 - 1))
        b.cyl("wood", pts[i], pts[i + 1], r0, r1, segs=6, rings=1, cap=(i in (0, 11)))
    b.cyl("cord", (0, -0.61, -0.004), (0, 0.61, -0.004), 0.0022, 0.0022, segs=4, rings=1, cap=False, twist_uv=6)
    b.wrap("leather", (0, 0, -0.11), (0, 1, 0), 0.018, 0.1, turns=4)
    return b.build("WEAPON_Bow", col)

def arrow(col):
    b = Builder(10)
    b.cyl("wood", (0, -0.36, 0), (0, 0.34, 0), 0.0045, 0.0045, segs=5, rings=3)
    b.blade("stone", (0, 0.335, 0), 0.05, 0.02, 0.005, point=1.0, seed=10, n_len=4, n_w=3)
    for k in range(3):   # fletching cards
        a = 2 * math.pi * k / 3; d = Vector((math.cos(a), 0, math.sin(a)))
        v = [b.bm.verts.new(Vector((0, y, 0)) + d * w) for y, w in ((-0.33, 0.0045), (-0.24, 0.0045), (-0.25, 0.02), (-0.33, 0.018))]
        f = b.bm.faces.new(v); b._map([f], "thatch", lambda co: (co.y * 4, 0.5))
    return b.build("WEAPON_Arrow", col)

def torch(col):
    b = Builder(11)
    b.cyl("wood", (0, -0.25, 0), (0, 0.28, 0), 0.018, 0.02, segs=8, rings=5, jitter=0.1)
    b.cyl("thatch", (0, 0.2, 0), (0, 0.34, 0), 0.032, 0.026, segs=8, rings=3, jitter=0.25)
    b.wrap("cord", (0, 0.2, 0), (0, 1, 0), 0.03, 0.03, turns=3)
    return b.build("TOOL_Torch", col)

def waterskin(col):
    b = Builder(12)
    b.blob("gourd", (0, 0.06, 0), (0.06, 0.085, 0.06), seed=12, rough=0.05, subdiv=2, facet=False)
    b.blob("gourd", (0, 0.17, 0), (0.03, 0.045, 0.03), seed=13, rough=0.04, subdiv=2, facet=False)
    b.cyl("wood", (0, 0.2, 0), (0, 0.235, 0), 0.012, 0.011, segs=6, rings=1)
    b.wrap("cord", (0, 0.135, 0), (0, 1, 0), 0.028, 0.02, turns=3)
    return b.build("ITEM_WaterContainer", col)

def campfire(col):
    b = Builder(13)
    for k in range(9):
        a = 2 * math.pi * k / 9 + random.uniform(-0.1, 0.1); r = 0.42
        b.blob("stone", (math.cos(a) * r, math.sin(a) * r, 0.06), (0.11, 0.09, 0.08), seed=20 + k, rough=0.2, subdiv=1)
    for k in range(5):   # logs leaning into a cone
        a = 2 * math.pi * k / 5 + 0.3
        p0 = Vector((math.cos(a) * 0.3, math.sin(a) * 0.3, 0.03)); p1 = Vector((math.cos(a) * 0.05, math.sin(a) * 0.05, 0.32))
        b.cyl("wood", p0, p1, 0.035, 0.028, segs=6, rings=2, jitter=0.1)
    # ash bed (blender Z up here: prop is built lying on the XY ground plane)
    g = bmesh.ops.create_circle(b.bm, cap_ends=True, segments=12, radius=0.33)
    for v in g["verts"]: v.co.z = 0.01
    b._map([f for f in b.bm.faces if all(v in g["verts"] for v in f.verts)], "stone", lambda co: (co.x * 2, co.y * 2))
    o = b.build("PROP_Campfire", col)
    return o

def shelter(col):
    b = Builder(14)
    H = 1.55; W = 2.4; D = 1.9
    for x in (-W / 2, W / 2):
        b.cyl("wood", (x, 0, 0), (x, 0, H + 0.1), 0.05, 0.04, segs=6, rings=3, jitter=0.1)                     # forked posts
        b.cyl("wood", (x, 0, H - 0.05), (x + 0.12 * (1 if x > 0 else -1), 0, H + 0.2), 0.03, 0.02, segs=5, rings=1)
    b.cyl("wood", (-W / 2 - 0.15, 0, H), (W / 2 + 0.15, 0, H), 0.045, 0.04, segs=6, rings=4)                    # ridge pole
    for i in range(7):
        x = -W / 2 + W * i / 6
        b.cyl("wood", (x, -0.05, H), (x, D, 0.02), 0.028, 0.025, segs=5, rings=3, jitter=0.1)                  # rafters to the ground (+Y = back)
    # thatch roof: layered strips
    for row in range(5):
        t0 = row / 5; t1 = (row + 1.25) / 5
        z0 = H * (1 - t0) + 0.05; z1 = H * (1 - t1) + 0.05; y0 = D * t0 - 0.05; y1 = D * t1 - 0.05
        vs = []
        for i in range(9):
            x = -W / 2 - 0.2 + (W + 0.4) * i / 8; jz = random.uniform(-0.03, 0.03)
            vs.append((b.bm.verts.new((x, y0, z0 + 0.04 + jz)), b.bm.verts.new((x, y1 + random.uniform(0, 0.08), z1 + 0.02 + jz))))
        fs = []
        for i in range(8):
            fs.append(b.bm.faces.new((vs[i][0], vs[i + 1][0], vs[i + 1][1], vs[i][1])))
        b._map(fs, "thatch", lambda co: (co.x * 0.4, co.y * 0.6 + co.z * 0.3))
    # floor mat
    fv = [b.bm.verts.new(p) for p in ((-0.9, 0.1, 0.02), (0.9, 0.1, 0.02), (0.9, 1.5, 0.02), (-0.9, 1.5, 0.02))]
    f = b.bm.faces.new(fv); b._map([f], "thatch", lambda co: (co.x * 0.5, co.y * 0.5))
    return b.build("PROP_Shelter", col)

def storage(col):
    b = Builder(15)
    rows = b.cyl("thatch", (0, 0, 0), (0, 0, 0.42), 0.26, 0.3, segs=12, rings=5, jitter=0.03, twist_uv=2)
    b.cyl("thatch", (0, 0, 0.42), (0, 0, 0.47), 0.31, 0.2, segs=12, rings=1, twist_uv=2)   # lid
    b.wrap("cord", (0, 0, 0.2), (0, 0, 1), 0.285, 0.2, turns=2, thick=0.006)
    return b.build("PROP_Storage", col)

def bedroll(col):
    b = Builder(16)
    fv = []
    for i in range(9):
        for j in range(5):
            x = -0.35 + 0.7 * j / 4; y = -0.95 + 1.9 * i / 8; z = 0.03 + 0.015 * noise.noise(Vector((x * 5, y * 5, 1)))
            fv.append(b.bm.verts.new((x, y, z)))
    fs = [b.bm.faces.new((fv[i * 5 + j], fv[i * 5 + j + 1], fv[(i + 1) * 5 + j + 1], fv[(i + 1) * 5 + j])) for i in range(8) for j in range(4)]
    b._map(fs, "leather", lambda co: (co.x * 0.8, co.y * 0.5))
    b.cyl("leather", (-0.36, -0.95, 0.08), (0.36, -0.95, 0.08), 0.07, 0.07, segs=8, rings=2)     # rolled head end
    return b.build("PROP_Bedroll", col)

def resource_items(col):
    out = []
    b = Builder(20); [b.cyl("wood", (random.uniform(-0.05, 0.05), -0.25, i * 0.06), (random.uniform(-0.05, 0.05), 0.25, i * 0.06 + 0.02), 0.035, 0.03, segs=7, rings=2, jitter=0.12) for i in range(3)]
    b.wrap("cord", (0, 0, 0.06), (0, 1, 0), 0.09, 0.04, turns=2, thick=0.006); out.append(b.build("ITEM_Wood", col))
    b = Builder(21); b.blob("stone", (0, 0, 0.05), (0.08, 0.07, 0.055), seed=21, rough=0.22); out.append(b.build("ITEM_Stone", col))
    b = Builder(22)
    for k in range(14):
        a = random.uniform(-0.3, 0.3); b.cyl("thatch", (0, 0, 0), (math.sin(a) * 0.3, 0, math.cos(a) * 0.35), 0.006, 0.003, segs=4, rings=2, cap=False)
    b.wrap("cord", (0, 0, 0.12), (0, 0, 1), 0.03, 0.03, turns=2); out.append(b.build("ITEM_Fiber", col))
    b = Builder(23)
    for k in range(9):
        b.blob("berries", (random.uniform(-0.03, 0.03), random.uniform(-0.03, 0.03), 0.02 + random.uniform(0, 0.04)), (0.011, 0.011, 0.011), seed=30 + k, rough=0.05, subdiv=1, facet=False)
    out.append(b.build("ITEM_Berries", col))
    b = Builder(24); b.blob("meat_raw", (0, 0, 0.04), (0.1, 0.07, 0.035), seed=24, rough=0.15, facet=False); b.cyl("bone", (-0.13, 0, 0.04), (0.14, 0, 0.04), 0.012, 0.012, segs=6, rings=1)
    out.append(b.build("ITEM_RawMeat", col))
    b = Builder(25); b.blob("meat_cooked", (0, 0, 0.04), (0.095, 0.065, 0.032), seed=24, rough=0.18, facet=False); b.cyl("bone", (-0.13, 0, 0.04), (0.14, 0, 0.04), 0.012, 0.012, segs=6, rings=1)
    out.append(b.build("ITEM_CookedMeat", col))
    b = Builder(26); fv = [b.bm.verts.new((x, y, 0.01 + 0.01 * noise.noise(Vector((x * 6, y * 6, 2))))) for y in (-0.2, 0, 0.2) for x in (-0.25, 0, 0.25)]
    fs = [b.bm.faces.new((fv[i * 3 + j], fv[i * 3 + j + 1], fv[(i + 1) * 3 + j + 1], fv[(i + 1) * 3 + j])) for i in range(2) for j in range(2)]
    b._map(fs, "leather", lambda co: (co.x, co.y)); out.append(b.build("ITEM_Hide", col))
    b = Builder(27); b.cyl("bone", (-0.14, 0, 0.02), (0.14, 0, 0.02), 0.016, 0.016, segs=6, rings=3, jitter=0.1)
    b.blob("bone", (-0.15, 0, 0.02), (0.028, 0.03, 0.025), seed=27, rough=0.1, subdiv=1); b.blob("bone", (0.15, 0, 0.02), (0.028, 0.03, 0.025), seed=28, rough=0.1, subdiv=1)
    out.append(b.build("ITEM_Bone", col))
    b = Builder(28)
    for k in range(4): b.cyl("cord", (0, 0, 0.03), (0, 0, 0.05), 0.07 - k * 0.012, 0.07 - k * 0.012, segs=12, rings=1, cap=False, twist_uv=3)
    out.append(b.build("ITEM_Rope", col))
    return out

def build_all():
    col = bpy.data.collections.get("TOOLS") or bpy.data.collections.new("TOOLS")
    if col.name not in bpy.context.scene.collection.children: bpy.context.scene.collection.children.link(col)
    objs = [hand_stone(col), flint_knife(col), stone_hammer(col), torch(col), stone_axe(col), stone_pick(col), spear(col), bow(col), arrow(col),
            waterskin(col), campfire(col), shelter(col), storage(col), bedroll(col)] + resource_items(col)
    return objs
