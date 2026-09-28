# PRIMAL FRONTIER - WEAPON_FlintSword (original design): a flat hardwood blade edged on both sides with knapped flint
# teeth, rawhide-wrapped grip, cord collar and a short wooden guard. Tool convention (pf_tools): grip centre at the
# origin, handle along +Y, blade towards +Y, edges towards +-Z (edge forward = +Z), flat faces +-X.
import bpy, bmesh, math, random, os
from mathutils import Vector, noise
import pf_tools as T

UNITY = r"E:\LAST OF THE PRIMAL\Assets\_Project"

def paddle(b, y0, y1, w0, wmax, thick, n_len=14, n_w=6):
    """flat blade: widens to wmax at 60 %, rounded tip; lens-shaped section (thinner at the edges)"""
    verts = {}
    L = y1 - y0
    def width(t):
        w = w0 + (wmax - w0) * min(1.0, t / 0.6)
        if t > 0.82: w *= math.sqrt(max(0.0, 1 - ((t - 0.82) / 0.18) ** 2))
        return max(w, 0.006)
    for i in range(n_len + 1):
        t = i / n_len; w = width(t)
        for j in range(n_w + 1):
            s = j / n_w * 2 - 1
            z = w * s * 0.5
            th = thick * (1 - 0.72 * s * s) * (1 - 0.25 * t)
            jit = 0.0012 * noise.noise(Vector((t * 11, s * 5, 3.0)))
            y = y0 + L * t
            verts[(i, j, 0)] = b.bm.verts.new(Vector((th * 0.5 + jit, y, z)))
            verts[(i, j, 1)] = b.bm.verts.new(Vector((-th * 0.5 - jit, y, z)))
    faces = []
    for i in range(n_len):
        for j in range(n_w):
            faces.append(b.bm.faces.new((verts[(i, j, 0)], verts[(i + 1, j, 0)], verts[(i + 1, j + 1, 0)], verts[(i, j + 1, 0)])))
            faces.append(b.bm.faces.new((verts[(i, j + 1, 1)], verts[(i + 1, j + 1, 1)], verts[(i + 1, j, 1)], verts[(i, j, 1)])))
        for j in (0, n_w):
            a0, a1, b0, b1 = verts[(i, j, 0)], verts[(i + 1, j, 0)], verts[(i, j, 1)], verts[(i + 1, j, 1)]
            faces.append(b.bm.faces.new((a0, a1, b1, b0) if j == 0 else (a0, b0, b1, a1)))
    for j in range(n_w):
        faces.append(b.bm.faces.new((verts[(0, j, 1)], verts[(0, j, 0)], verts[(0, j + 1, 0)], verts[(0, j + 1, 1)])))
        faces.append(b.bm.faces.new((verts[(n_len, j, 0)], verts[(n_len, j, 1)], verts[(n_len, j + 1, 1)], verts[(n_len, j + 1, 0)])))
    b._map(faces, "wood", lambda co: (co.z * 6 + 0.5, (co.y - y0) * 1.6))
    return width

def build(col=None):
    b = T.Builder(21); random.seed(21)
    # grip, pommel, rawhide wrap
    b.cyl("wood", (0, -0.118, 0), (0, 0.112, 0), 0.0155, 0.0165, segs=8, rings=4, jitter=0.04)
    b.blob("stone", (0, -0.13, 0), (0.021, 0.018, 0.021), seed=21, rough=0.16, subdiv=1)
    b.wrap("leather", (0, -0.005, 0), (0, 1, 0), 0.0165, 0.2, turns=9, thick=0.0028)
    # guard across the edges, cord collar
    b.cyl("wood", (0, 0.118, -0.052), (0, 0.118, 0.052), 0.011, 0.011, segs=6, rings=2, jitter=0.08)
    b.wrap("cord", (0, 0.135, 0), (0, 1, 0), 0.019, 0.03, turns=4, thick=0.003)
    # hardwood blade with flint teeth along both edges
    y0, y1 = 0.12, 0.8
    width = paddle(b, y0, y1, 0.05, 0.078, 0.017)
    n = 8
    for side in (1, -1):
        for k in range(n):
            t = 0.1 + 0.72 * k / (n - 1) + random.uniform(-0.015, 0.015)
            w = width(t)
            y = y0 + (y1 - y0) * t
            b.blob("stone", (0, y, side * (w * 0.5 + 0.004)), (0.0035, 0.013 * random.uniform(0.85, 1.15), 0.011 * random.uniform(0.9, 1.25)),
                   seed=40 + k + (0 if side > 0 else 20), rough=0.3, subdiv=1)
    # tip tooth
    b.blob("stone", (0, y1 + 0.004, 0), (0.0035, 0.014, 0.012), seed=77, rough=0.3, subdiv=1)
    o = b.build("WEAPON_FlintSword", col)
    return o

def export(o):
    import pf_export
    path = os.path.join(UNITY, "Art", "Models", "Weapons", "WEAPON_FlintSword.fbx")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(filepath=path, **pf_export.FBX_KW)
    return path

def icon(o):
    import pf_icons
    p = os.path.join(UNITY, "Art", "Icons", "ICON_FlintSword.png")
    os.makedirs(os.path.dirname(p), exist_ok=True)
    return pf_icons.render_object(o, p, size=256, yaw=90, pitch=10, roll=-45, margin=1.1)
