#!/usr/bin/env python3
"""Step 1: download each molecule's 3D conformer from PubChem into structures.json.

structures.json is committed, so this only needs re-running when molecules.json gains a
molecule. Exits non-zero if a download fails or its formula differs from the manifest.
"""
import collections
import json
import pathlib
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

HERE = pathlib.Path(__file__).resolve().parent
URL = "https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/{}/SDF?record_type=3d"


def hill_formula(elements):
    counts = collections.Counter(elements)
    order = sorted(counts, key=lambda e: (e != "C", e != "H", e)) if "C" in counts else sorted(counts)
    return "".join(f"{e}{counts[e] if counts[e] > 1 else ''}" for e in order)


def parse_sdf(text):
    """Parse a V2000 molfile block into atoms [element, x, y, z] and bonds [a, b, order]."""
    lines = text.splitlines()
    atom_count, bond_count = int(lines[3][:3]), int(lines[3][3:6])
    atoms = [[l[31:34].strip(), float(l[0:10]), float(l[10:20]), float(l[20:30])]
             for l in lines[4:4 + atom_count]]
    bonds = [[int(l[0:3]) - 1, int(l[3:6]) - 1, int(l[6:9])]
             for l in lines[4 + atom_count:4 + atom_count + bond_count]]
    return lines[0].strip(), atoms, bonds


def main():
    manifest = json.loads((HERE / "molecules.json").read_text(encoding="utf-8"))
    out, failed = {}, []
    for item_id, spec in manifest["molecules"].items():
        if "structure" in spec:  # PubChem has no 3D conformer for this one
            out[item_id] = spec["structure"]
            print(f"{item_id}: inline structure from molecules.json")
            continue
        url = URL.format(urllib.parse.quote(spec["pubchem"]))
        try:
            with urllib.request.urlopen(url, timeout=30) as resp:
                cid, atoms, bonds = parse_sdf(resp.read().decode("ascii"))
        except (urllib.error.URLError, ValueError, IndexError) as exc:
            print(f"{item_id}: FAILED {spec['pubchem']}: {exc}")
            failed.append(item_id)
            continue
        formula = hill_formula(a[0] for a in atoms)
        if formula != spec["formula"]:
            print(f"{item_id}: FORMULA MISMATCH got {formula}, want {spec['formula']} (CID {cid})")
            failed.append(item_id)
            continue
        print(f"{item_id}: CID {cid} {formula} atoms={len(atoms)} bonds={len(bonds)}")
        out[item_id] = {"cid": cid, "atoms": atoms, "bonds": bonds}
        time.sleep(0.25)  # PubChem asks for at most 5 requests per second
    (HERE / "structures.json").write_text(json.dumps(out, indent=1), encoding="utf-8")
    if failed:
        sys.exit(f"Failed: {', '.join(failed)}")


if __name__ == "__main__":
    main()
