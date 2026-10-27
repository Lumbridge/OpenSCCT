"""Score golden renders of the rewrite against captures of the original.

    python -I -B tools/golden/score.py [--map SteeE] [--game local/golden/game] [--svm local/golden/svm]
        [--out local/golden/score] [--sbs] [--bin 4]

For every pose that has both <game>/<Map>/<pose>.png and <svm>/<Map>/<pose>.png (same size), on display
luma (Rec. 601 weights over the stored 8-bit values):
- SSIM: Gaussian window (sigma 1.5, 11 taps), C1 = (0.01 L)^2, C2 = (0.03 L)^2, mean over the picture;
- luma ratio: mean luma of ours / mean luma of the original;
- dark corners: mean luma of the four corner blocks (a tenth of each side), ours minus the original, 0..1.

With --bin N the SSIM is taken on N x N block means of both pictures: vision modes multiply every pixel by a
random grain that changes each frame, which per-pixel SSIM would score as structure.

Prints a table, the median and minimum SSIM, and the Faithful gates of the plan (median SSIM >= 0.90,
min >= 0.85, luma ratio 0.95..1.05, dark-corner difference within 0.02). Writes <out>/<Map>/score.json, and with
--sbs a side by side (original | ours | difference x4) per pose. Exit code 0 always; the gates are reported.
Pictures and scores are game-derived data: they stay under local/.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))

GATES = {'ssimMedian': 0.90, 'ssimMin': 0.85, 'lumaLow': 0.95, 'lumaHigh': 1.05, 'darkCorner': 0.02}


def luma(path):
    rgb = np.asarray(Image.open(path).convert('RGB'), dtype=np.float64)
    return rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114


def gaussian_kernel(sigma=1.5, taps=11):
    x = np.arange(taps, dtype=np.float64) - (taps - 1) / 2.0
    k = np.exp(-(x * x) / (2.0 * sigma * sigma))
    return k / k.sum()


def blur(img, kernel):
    """Separable 'valid' convolution: the SSIM maps only where the window fits inside the picture."""
    t = len(kernel)
    rows = sum(kernel[i] * img[:, i:img.shape[1] - t + 1 + i] for i in range(t))
    return sum(kernel[i] * rows[i:rows.shape[0] - t + 1 + i, :] for i in range(t))


def ssim(a, b, data_range=255.0):
    k = gaussian_kernel()
    c1 = (0.01 * data_range) ** 2
    c2 = (0.03 * data_range) ** 2
    mu_a, mu_b = blur(a, k), blur(b, k)
    aa, bb, ab = blur(a * a, k), blur(b * b, k), blur(a * b, k)
    var_a, var_b, cov = aa - mu_a * mu_a, bb - mu_b * mu_b, ab - mu_a * mu_b
    s = ((2 * mu_a * mu_b + c1) * (2 * cov + c2)) / ((mu_a * mu_a + mu_b * mu_b + c1) * (var_a + var_b + c2))
    return float(s.mean())


def binned(img, n):
    """Mean of each n x n block (the picture cropped to whole blocks); n <= 1 returns the picture."""
    if n <= 1:
        return img
    h, w = (img.shape[0] // n) * n, (img.shape[1] // n) * n
    return img[:h, :w].reshape(h // n, n, w // n, n).mean(axis=(1, 3))


def corners(img):
    h, w = img.shape
    bh, bw = max(1, h // 10), max(1, w // 10)
    blocks = [img[:bh, :bw], img[:bh, -bw:], img[-bh:, :bw], img[-bh:, -bw:]]
    return float(np.mean([b.mean() for b in blocks]) / 255.0)


def side_by_side(game_path, svm_path, out_path):
    a = Image.open(game_path).convert('RGB')
    b = Image.open(svm_path).convert('RGB')
    d = np.clip(np.abs(np.asarray(a, dtype=np.int16) - np.asarray(b, dtype=np.int16)) * 4, 0, 255).astype(np.uint8)
    w, h = a.size
    sbs = Image.new('RGB', (w * 3, h))
    sbs.paste(a, (0, 0))
    sbs.paste(b, (w, 0))
    sbs.paste(Image.fromarray(d), (w * 2, 0))
    sbs.save(out_path)


def score(game_dir, svm_dir, out_dir=None, sbs=False, bin_size=1):
    rows = []
    poses = sorted(f[:-4] for f in os.listdir(game_dir) if f.endswith('.png'))
    for pose in poses:
        g, s = os.path.join(game_dir, pose + '.png'), os.path.join(svm_dir, pose + '.png')
        if not os.path.isfile(s):
            continue
        a, b = luma(g), luma(s)
        if a.shape != b.shape:
            rows.append({'pose': pose, 'error': 'size %s vs %s' % (a.shape, b.shape)})
            continue
        mean_a, mean_b = a.mean(), b.mean()
        row = {'pose': pose, 'ssim': round(ssim(binned(a, bin_size), binned(b, bin_size)), 4),
               'lumaRatio': round(float(mean_b / mean_a), 4) if mean_a > 0 else None,
               'lumaGame': round(float(mean_a / 255.0), 4), 'lumaSvm': round(float(mean_b / 255.0), 4),
               'darkCorner': round(corners(b) - corners(a), 4)}
        rows.append(row)
        if sbs and out_dir:
            side_by_side(g, s, os.path.join(out_dir, pose + '_sbs.png'))
    good = [r for r in rows if 'ssim' in r]
    values = sorted(r['ssim'] for r in good)
    summary = {}
    if values:
        mid = len(values) // 2
        median = values[mid] if len(values) % 2 else (values[mid - 1] + values[mid]) / 2.0
        ratios = [r['lumaRatio'] for r in good if r['lumaRatio'] is not None]
        summary = {
            'poses': len(good), 'ssimMedian': round(median, 4), 'ssimMin': round(values[0], 4),
            'lumaRatioMedian': round(float(np.median(ratios)), 4) if ratios else None,
            'gates': {
                'ssimMedian': median >= GATES['ssimMedian'],
                'ssimMin': values[0] >= GATES['ssimMin'],
                'luma': all(GATES['lumaLow'] <= r <= GATES['lumaHigh'] for r in ratios),
                'darkCorner': all(abs(r['darkCorner']) <= GATES['darkCorner'] for r in good),
            },
        }
    return rows, summary


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('--map', default='SteeE')
    parser.add_argument('--game', default=os.path.join(REPO, 'local', 'golden', 'game'))
    parser.add_argument('--svm', default=os.path.join(REPO, 'local', 'golden', 'svm'))
    parser.add_argument('--out', default=os.path.join(REPO, 'local', 'golden', 'score'))
    parser.add_argument('--sbs', action='store_true', help='write side-by-side pictures')
    parser.add_argument('--bin', type=int, default=1, help='SSIM on N x N block means (vision modes: 4)')
    args = parser.parse_args()
    game_dir = os.path.join(args.game, args.map)
    svm_dir = os.path.join(args.svm, args.map)
    out_dir = os.path.join(args.out, args.map)
    os.makedirs(out_dir, exist_ok=True)
    rows, summary = score(game_dir, svm_dir, out_dir, args.sbs, args.bin)
    print('%-12s %7s %7s %7s %7s %8s' % ('pose', 'ssim', 'luma', 'game', 'svm', 'corner'))
    for r in rows:
        if 'error' in r:
            print('%-12s %s' % (r['pose'], r['error']))
            continue
        print('%-12s %7.4f %7.3f %7.4f %7.4f %+8.4f' % (r['pose'], r['ssim'], r['lumaRatio'] or 0.0, r['lumaGame'],
                                                       r['lumaSvm'], r['darkCorner']))
    if summary:
        print('median SSIM %.4f, min %.4f, median luma ratio %s' % (summary['ssimMedian'], summary['ssimMin'],
                                                                   summary['lumaRatioMedian']))
        print('gates: ' + ', '.join('%s %s' % (k, 'pass' if v else 'FAIL') for k, v in summary['gates'].items()))
    render = os.path.join(svm_dir, 'render.json')
    extra = {}
    if os.path.isfile(render):
        with open(render, encoding='utf-8') as f:
            extra = json.load(f)
    with open(os.path.join(out_dir, 'score.json'), 'w', encoding='utf-8') as f:
        json.dump({'map': args.map, 'bin': args.bin, 'rows': rows, 'summary': summary, 'render': extra}, f, indent=1)
    return 0


if __name__ == '__main__':
    sys.exit(main())
