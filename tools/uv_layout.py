"""
Maintainer tool: derive the skin geometry table (SkinGeometry.cs) from the game's player mesh.

    mcd2skin iodump SK_Player_Master <dir>        # copy the mesh package out of your own game
    python tools/uv_layout.py <dir>/SK_Player_Master.uasset

Every texel the mesh samples is traced to its 3D point and face normal. That gives each face's part,
direction and orientation "as seen from outside", printed as C# FaceMap lines. It also prints the
face-animation quads (which corner texel lands on which face pixel). Run it after a game update and
compare with SkinGeometry.cs. Requires numpy.

Mesh space: x+ = the hero's left, y+ = front, z+ = up; 6.25 units per skin pixel. The byte offsets
below are for game 1.1.1.0 and are checked against the expected element counts before use.
"""
import struct
import sys
import collections
import numpy as np

OFFSETS = dict(idx_count=5269, idx_data=5273, pos_n2=9905, uv_n=22099)
NV, NI = 608, 2310
REGIONS = {'head': (0, 0, 32, 16), 'hat': (32, 0, 64, 16), 'body': (16, 16, 40, 32), 'armA': (40, 16, 56, 32),
           'armB': (32, 48, 48, 64), 'legA': (0, 16, 16, 32), 'legB': (16, 48, 32, 64)}
# viewer's (right, down) vectors for each outward face direction
VIEW = {'y+': ((1, 0, 0), (0, 0, -1)), 'y-': ((-1, 0, 0), (0, 0, -1)), 'x-': ((0, 1, 0), (0, 0, -1)),
        'x+': ((0, -1, 0), (0, 0, -1)), 'z+': ((1, 0, 0), (0, 1, 0)), 'z-': ((1, 0, 0), (0, -1, 0))}
FACE = {'y+': 'Front', 'y-': 'Back', 'x-': 'Right', 'x+': 'Left', 'z+': 'Top', 'z-': 'Bottom'}


def load(path):
    b = open(path, 'rb').read()
    u32 = lambda o: struct.unpack_from('<I', b, o)[0]
    if u32(OFFSETS['idx_count']) != NI or u32(OFFSETS['pos_n2']) != NV or u32(OFFSETS['uv_n']) != NV:
        sys.exit('SK_Player_Master layout changed: update OFFSETS first')
    idx = np.frombuffer(b, '<u2', NI, OFFSETS['idx_data']).reshape(-1, 3)
    pos = np.frombuffer(b, '<f4', NV * 3, OFFSETS['pos_n2'] + 4).reshape(-1, 3).astype(float) / 6.25
    uv = np.frombuffer(b, '<f2', NV * 2, OFFSETS['uv_n'] + 4).reshape(-1, 2).astype(float) * 64
    return idx, pos, uv


def region(c):
    if c[0] < 8 and c[1] < 8:
        return 'palette'
    for k, (u0, v0, u1, v1) in REGIONS.items():
        if u0 < c[0] < u1 and v0 < c[1] < v1:
            return k
    return '?'


def main(path):
    idx, pos, uv = load(path)
    cen = uv[idx].mean(1)
    regs = [region(c) for c in cen]
    texels = collections.defaultdict(dict)
    for t, r in zip(idx, regs):
        if r in ('palette', '?'):
            continue
        P, U = pos[t], uv[t]
        n = np.cross(P[1] - P[0], P[2] - P[0])
        if np.linalg.norm(n) < 1e-9:
            continue
        n /= np.linalg.norm(n)
        ctr = pos[np.unique(idx[[x == r for x in regs]])].mean(0)
        if np.dot(n, P.mean(0) - ctr) < 0:
            n = -n
        ax = int(np.argmax(np.abs(n)))
        key = (r, 'xyz'[ax] + ('+' if n[ax] > 0 else '-'))
        det = (U[1, 1] - U[2, 1]) * (U[0, 0] - U[2, 0]) + (U[2, 0] - U[1, 0]) * (U[0, 1] - U[2, 1])
        if abs(det) < 1e-9:
            continue
        for ty in range(int(U[:, 1].min()), int(np.ceil(U[:, 1].max()))):
            for tx in range(int(U[:, 0].min()), int(np.ceil(U[:, 0].max()))):
                qx, qy = tx + .5, ty + .5
                l0 = ((U[1, 1] - U[2, 1]) * (qx - U[2, 0]) + (U[2, 0] - U[1, 0]) * (qy - U[2, 1])) / det
                l1 = ((U[2, 1] - U[0, 1]) * (qx - U[2, 0]) + (U[0, 0] - U[2, 0]) * (qy - U[2, 1])) / det
                if min(l0, l1, 1 - l0 - l1) < -1e-6:
                    continue
                texels[key][(tx, ty)] = l0 * P[0] + l1 * P[1] + (1 - l0 - l1) * P[2]

    for (r, f), d in sorted(texels.items()):
        # which part: arms/legs by side (x- = the hero's right)
        side = np.array(list(d.values()))[:, 0].mean()
        part = {'head': 'Head', 'hat': 'Hat', 'body': 'Body'}.get(r) or \
            (('Right' if side < 0 else 'Left') + ('Arm' if r.startswith('arm') else 'Leg'))
        R, D = np.array(VIEW[f][0]), np.array(VIEW[f][1])
        items = [(k, (p @ R, p @ D)) for k, p in d.items()]
        c0 = min(v[0] for _, v in items); r0 = min(v[1] for _, v in items)
        m = {(int(round(v[0] - c0)), int(round(v[1] - r0))): k for k, v in items}
        W = max(c for c, _ in m) + 1; H = max(rr for _, rr in m) + 1
        tx0 = min(k[0] for k in d); ty0 = min(k[1] for k in d)
        flips = [(fx, fy) for fx in (0, 1) for fy in (0, 1)
                 if all(m[(c, rr)] == (tx0 + (W - 1 - c if fx else c), ty0 + (H - 1 - rr if fy else rr))
                        for c in range(W) for rr in range(H))]
        if not flips:
            print(f'// {part} {FACE[f]}: not a plain rectangle, check by hand')
            continue
        fx, fy = flips[0]
        print(f'new(Part.{part}, Face.{FACE[f]}, {tx0}, {ty0}, {W}, {H}, {str(bool(fx)).lower()}, {str(bool(fy)).lower()}),')

    print('\n// face animation quads: texel -> face pixel (col from the viewer\'s left, row from the top)')
    for t, r in zip(idx, regs):
        if r == 'palette':
            print('  '.join('uv(%.0f,%.0f)->face(%.2f,%.2f)' % (uv[v][0], uv[v][1], pos[v][0] + 4, 32 - pos[v][2]) for v in t))


if __name__ == '__main__':
    main(sys.argv[1])
