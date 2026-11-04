"""Round-trip every source-bound EAX3 listener block through the original x86 probe.

This mode creates silent buffers only. Rejected whole-block updates are recorded
separately from infrastructure failures and verified to leave the old state intact.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import subprocess


def probe_blocks(executable: Path, dll: Path, parameters: Path, output: Path):
    schema = json.loads(parameters.read_text(encoding="utf-8"))
    rows = []
    for environment in schema["environments"]:
        file = Path(environment["blockFile"])
        process = subprocess.run([str(executable.resolve()), str(dll.resolve()), str(file)],
                                 capture_output=True, text=True, timeout=15, check=False)
        result = json.loads(process.stdout)
        accepted = result.get("EAX3SetAll") == "0x00000000" and result.get("returnedDifferentBytes") == 0
        before = Path(str(file) + ".vendor-default.bin")
        after = Path(str(file) + ".vendor-returned.bin")
        retained = before.exists() and after.exists() and before.read_bytes() == after.read_bytes()
        rejected = result.get("EAX3SetAll") == "0x80070057" and retained
        if result.get("audioPlayed") or result.get("microphoneOpened"):
            raise RuntimeError("Silent property probe unexpectedly entered an audio/capture path")
        row = {"id": environment["id"], "sourceClass": environment["sourceClass"], "locations": environment["locations"],
               "blockSha256": environment["blockSha256"], "status": "accepted" if accepted else "rejected" if rejected else "failed",
               "rejectedStateUnchanged": retained if rejected else None, "exitCode": process.returncode, "result": result}
        rows.append(row)
    report = {"format": "openscct.eax3-property-sweep/1", "tested": len(rows),
              "accepted": sum(r["status"] == "accepted" for r in rows),
              "rejected": sum(r["status"] == "rejected" for r in rows),
              "failed": sum(r["status"] == "failed" for r in rows), "rows": rows}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--probe", type=Path, required=True)
    parser.add_argument("--dll", type=Path, required=True)
    parser.add_argument("--parameters", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    report = probe_blocks(args.probe, args.dll, args.parameters, args.out)
    print(json.dumps({k: report[k] for k in ("tested", "accepted", "rejected", "failed")}))
    raise SystemExit(1 if report["failed"] else 0)
