import numpy as np, struct
def rot_from_x(d):
    d = np.asarray(d, np.float32); d = d / np.linalg.norm(d); x = np.array([1, 0, 0], np.float32)
    v = np.cross(x, d); c = float(np.dot(x, d)); s = np.linalg.norm(v)
    if s < 1e-8: return np.eye(3, dtype=np.float32) if c > 0 else np.diag([-1, -1, 1]).astype(np.float32)
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]], np.float32)
    return (np.eye(3) + vx + vx @ vx * ((1 - c) / (s * s))).astype(np.float32)
def smin(a, b, k):
    if k <= 0: return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0, 1)
    return b * (1 - h) + a * h - k * h * (1 - h)
def ssub(d, sub, k):
    h = np.clip(0.5 - 0.5 * (d + sub) / k, 0, 1)
    return d * (1 - h) + (-sub) * h + k * h * (1 - h)
def write_ply(path, verts, faces):
    with open(path, 'wb') as f:
        f.write(f"ply\nformat binary_little_endian 1.0\nelement vertex {len(verts)}\nproperty float x\nproperty float y\nproperty float z\n"
                f"element face {len(faces)}\nproperty list uchar int vertex_indices\nend_header\n".encode())
        f.write(verts.astype('<f4').tobytes())
        fa = np.empty(len(faces), dtype=[('n', 'u1'), ('i', '<i4', 3)]); fa['n'] = 3; fa['i'] = faces[:, ::-1]
        f.write(fa.tobytes())
