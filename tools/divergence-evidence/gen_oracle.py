#!/usr/bin/env python3
"""Generate the h3-py (upstream libh3) oracle cell sets for the canonical
polygon matrix, per the divergence-investigation task spec.

Run with the pinned venv:
    .venv-h3oracle/bin/python divergence/gen_oracle.py
"""
from __future__ import annotations

import hashlib
import json
import os
import platform
import sys

import h3

MAX_CELLS = 500_000
CELLS_INLINE_LIMIT = 25_000

OUT_JSON = os.path.join(os.path.dirname(os.path.abspath(__file__)), "h3py-oracle.json")


def sha256_of_sorted(cells: list[str]) -> str:
    joined = "\n".join(cells)
    return hashlib.sha256(joined.encode("utf-8")).hexdigest()


def fill(shell, holes, res):
    poly = h3.LatLngPoly(shell, *holes)
    cells = h3.h3shape_to_cells(poly, res)
    return sorted(cells)


def make_result(case_id, res, cells, skipped=False, note=None):
    entry = {
        "case_id": case_id,
        "res": res,
        "impl": "h3py",
        "count": len(cells) if not skipped else None,
        "sha256": sha256_of_sorted(cells) if not skipped else None,
    }
    if skipped:
        entry["skipped"] = True
        entry["skip_reason"] = note
    else:
        if len(cells) <= CELLS_INLINE_LIMIT:
            entry["cells"] = cells
    return entry


def main() -> None:
    results = []
    skips = []
    meta_cases = {}

    # ------------------------------------------------------------------
    # 1. sf-triangle (verbatim from tests/H3.NET.Native.Benchmarks/H3Benchmarks.cs)
    # ------------------------------------------------------------------
    sf_triangle_shell = [
        (37.813318999983238, -122.4089866999972145),
        (37.7866302000007224, -122.3805436999997056),
        (37.7198061999978478, -122.3544736999993603),
    ]
    meta_cases["sf-triangle"] = {"shell": sf_triangle_shell, "holes": []}
    for res in (7, 8, 9, 10, 11):
        cells = fill(sf_triangle_shell, [], res)
        if len(cells) > MAX_CELLS:
            skips.append(("sf-triangle", res, len(cells)))
            results.append(make_result("sf-triangle", res, [], skipped=True,
                                        note=f"count {len(cells)} > {MAX_CELLS}"))
            continue
        results.append(make_result("sf-triangle", res, cells))

    # ------------------------------------------------------------------
    # 2. sweep-box (verbatim from tests/H3.NET.Native.Benchmarks/H3PolygonSweepBenchmarks.cs)
    # ------------------------------------------------------------------
    MinLat, MaxLat, MinLng, MaxLng = 37.525, 38.025, -122.668, -122.168
    sweep_box_shell = [
        (MinLat, MinLng),
        (MinLat, MaxLng),
        (MaxLat, MaxLng),
        (MaxLat, MinLng),
    ]
    meta_cases["sweep-box"] = {"shell": sweep_box_shell, "holes": []}
    for res in (4, 5, 6, 7, 8, 9, 10):
        cells = fill(sweep_box_shell, [], res)
        if len(cells) > MAX_CELLS:
            skips.append(("sweep-box", res, len(cells)))
            results.append(make_result("sweep-box", res, [], skipped=True,
                                        note=f"count {len(cells)} > {MAX_CELLS}"))
            continue
        results.append(make_result("sweep-box", res, cells))

    # ------------------------------------------------------------------
    # 3. lshape
    # ------------------------------------------------------------------
    lshape_shell = [
        (37.80, -122.46),
        (37.80, -122.40),
        (37.76, -122.40),
        (37.76, -122.43),
        (37.72, -122.43),
        (37.72, -122.46),
    ]
    meta_cases["lshape"] = {"shell": lshape_shell, "holes": []}
    for res in (7, 8, 9, 10):
        cells = fill(lshape_shell, [], res)
        if len(cells) > MAX_CELLS:
            skips.append(("lshape", res, len(cells)))
            results.append(make_result("lshape", res, [], skipped=True,
                                        note=f"count {len(cells)} > {MAX_CELLS}"))
            continue
        results.append(make_result("lshape", res, cells))

    # ------------------------------------------------------------------
    # 4. hole-box
    # ------------------------------------------------------------------
    holebox_outer = [
        (37.81, -122.47),
        (37.81, -122.37),
        (37.71, -122.37),
        (37.71, -122.47),
    ]
    holebox_hole = [
        (37.78, -122.44),
        (37.78, -122.40),
        (37.74, -122.40),
        (37.74, -122.44),
    ]
    meta_cases["hole-box"] = {"shell": holebox_outer, "holes": [holebox_hole]}
    for res in (7, 8, 9):
        cells = fill(holebox_outer, [holebox_hole], res)
        if len(cells) > MAX_CELLS:
            skips.append(("hole-box", res, len(cells)))
            results.append(make_result("hole-box", res, [], skipped=True,
                                        note=f"count {len(cells)} > {MAX_CELLS}"))
            continue
        results.append(make_result("hole-box", res, cells))

    # ------------------------------------------------------------------
    # 5. transmeridian
    # ------------------------------------------------------------------
    transmeridian_shell = [
        (5.0, 179.8),
        (5.0, -179.8),
        (4.6, -179.8),
        (4.6, 179.8),
    ]
    meta_cases["transmeridian"] = {"shell": transmeridian_shell, "holes": []}
    for res in (5, 6, 7):
        cells = fill(transmeridian_shell, [], res)
        if len(cells) > MAX_CELLS:
            skips.append(("transmeridian", res, len(cells)))
            results.append(make_result("transmeridian", res, [], skipped=True,
                                        note=f"count {len(cells)} > {MAX_CELLS}"))
            continue
        results.append(make_result("transmeridian", res, cells))

    # ------------------------------------------------------------------
    # 6. multi-2box: primary = union of two independent per-polygon fills.
    # Also record the LatLngMultiPoly API result separately for equality check.
    # ------------------------------------------------------------------
    boxA = [
        (37.80, -122.46),
        (37.80, -122.43),
        (37.77, -122.43),
        (37.77, -122.46),
    ]
    boxB = [
        (37.75, -122.41),
        (37.75, -122.38),
        (37.72, -122.38),
        (37.72, -122.41),
    ]
    meta_cases["multi-2box"] = {"boxA": boxA, "boxB": boxB}
    multiapi_notes = []
    for res in (8, 9, 10):
        cellsA = fill(boxA, [], res)
        cellsB = fill(boxB, [], res)
        union_sorted = sorted(set(cellsA) | set(cellsB))

        if len(union_sorted) > MAX_CELLS:
            skips.append(("multi-2box", res, len(union_sorted)))
            results.append(make_result("multi-2box", res, [], skipped=True,
                                        note=f"count {len(union_sorted)} > {MAX_CELLS}"))
        else:
            results.append(make_result("multi-2box", res, union_sorted))

        # multiapi variant
        multipoly = h3.LatLngMultiPoly(h3.LatLngPoly(boxA), h3.LatLngPoly(boxB))
        multiapi_cells = sorted(h3.h3shape_to_cells(multipoly, res))

        if len(multiapi_cells) > MAX_CELLS:
            skips.append(("multi-2box:multiapi", res, len(multiapi_cells)))
            results.append(make_result("multi-2box:multiapi", res, [], skipped=True,
                                        note=f"count {len(multiapi_cells)} > {MAX_CELLS}"))
        else:
            results.append(make_result("multi-2box:multiapi", res, multiapi_cells))

        equal = (union_sorted == multiapi_cells)
        multiapi_notes.append({
            "res": res,
            "union_count": len(union_sorted),
            "multiapi_count": len(multiapi_cells),
            "equal_to_union": equal,
            "union_sha256": sha256_of_sorted(union_sorted) if len(union_sorted) <= MAX_CELLS else None,
            "multiapi_sha256": sha256_of_sorted(multiapi_cells) if len(multiapi_cells) <= MAX_CELLS else None,
        })

    # ------------------------------------------------------------------
    # Assemble output
    # ------------------------------------------------------------------
    meta = {
        "h3_versions": h3.versions(),
        "h3_module_version": h3.__version__,
        "python_version": sys.version,
        "python_implementation": platform.python_implementation(),
        "platform": platform.platform(),
        "machine": platform.machine(),
        "cases": meta_cases,
        "skips": [
            {"case_id": c, "res": r, "count": n} for (c, r, n) in skips
        ],
        "multi_2box_multiapi_vs_union": multiapi_notes,
        "notes": (
            "Shell/holes are [(lat, lng), ...] degrees, DEFAULT center-containment "
            "via h3.h3shape_to_cells(h3.LatLngPoly(shell, *holes), res). Cell IDs are "
            "the lowercase hex strings h3-py returns (h3.api.basic_str representation, "
            "no leading zeros), sorted ordinal ascending. sha256 is over the sorted IDs "
            "joined with \\n (UTF-8). 'cells' field is omitted when count > "
            f"{CELLS_INLINE_LIMIT}; entries are 'skipped' entirely when count > {MAX_CELLS}."
        ),
    }

    output = {"meta": meta, "results": results}

    with open(OUT_JSON, "w") as f:
        json.dump(output, f, indent=2)

    print("h3-py version:", h3.__version__)
    print("h3 versions():", h3.versions())
    print("wrote:", OUT_JSON)
    print()
    print(f"{'case_id':<22}{'res':>4}  {'count':>10}  sha256[:12]")
    for r in results:
        if r.get("skipped"):
            print(f"{r['case_id']:<22}{r['res']:>4}  {'SKIPPED':>10}")
        else:
            print(f"{r['case_id']:<22}{r['res']:>4}  {r['count']:>10}  {r['sha256'][:12]}")

    if skips:
        print()
        print("SKIPS (count > 500,000):")
        for c, r, n in skips:
            print(f"  {c} res={r} count={n}")

    print()
    print("multi-2box union vs multiapi:")
    for n in multiapi_notes:
        print(f"  res={n['res']} union={n['union_count']} multiapi={n['multiapi_count']} equal={n['equal_to_union']}")


if __name__ == "__main__":
    main()
