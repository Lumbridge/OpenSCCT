"""One-client, loopback-only UDP fault injector for real network-lab game processes.

No game protocol is interpreted. Both directions, including connection and reliable
traffic, traverse independently delayed/lost/duplicated UDP datagrams. Runtime
controls are local files, never a network administration endpoint.
"""
import argparse
import heapq
import json
import os
from pathlib import Path
import random
import selectors
import socket
import time


PROFILES = {
    "lan": (0, 0, 0, 0, 0),
    "typical": (40, 40, 10, 1, 0),
    "rough": (100, 100, 30, 3, 0.5),
    "asymmetric": (40, 120, 20, 2, 0.5),
    "limited": (100, 100, 30, 3, 0.5),
}

# Bytes per second in each direction. Queueing is bounded to two seconds of traffic;
# overload drops datagrams instead of silently creating an unlimited lag reservoir.
PROFILE_RATES = {"limited": {"up": 5000, "down": 12000}}


def atomic(path, value):
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value), encoding="utf-8")
    for attempt in range(6):
        try:
            os.replace(temp, path)
            break
        except PermissionError:
            if attempt == 5:
                raise
            time.sleep(0.005)


def run(args):
    directory = Path(args.run_directory).resolve(strict=True)
    rng = random.Random(args.seed)
    up_ms, down_ms, jitter_ms, loss, duplicate = PROFILES[args.profile]
    rates = PROFILE_RATES.get(args.profile, {"up": 0, "down": 0})
    next_wire = {"up": 0.0, "down": 0.0}
    counters = {direction: dict(received=0, sent=0, dropped=0, duplicated=0, bytes=0)
                for direction in ("up", "down")}
    queue = []
    serial = 0
    maximum_queue = 0
    client = None
    blackout_until = 0
    down_blackout_until = 0
    down_blackout_end_ms = 0
    control_id = None
    started = time.monotonic()
    next_status = 0
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as front, \
            socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as back, selectors.DefaultSelector() as selector:
        front.bind(("127.0.0.1", 0))
        back.bind(("127.0.0.1", 0))
        back.connect(("127.0.0.1", args.server_port))
        for sock, direction in ((front, "up"), (back, "down")):
            sock.setblocking(False)
            selector.register(sock, selectors.EVENT_READ, direction)
        atomic(directory / "proxy-ready.json", dict(port=front.getsockname()[1], pid=os.getpid(),
               profile=args.profile, seed=args.seed, upMs=up_ms, downMs=down_ms,
               jitterMs=jitter_ms, lossPercent=loss, duplicatePercent=duplicate, bytesPerSecond=rates))
        while time.monotonic() - started < args.seconds:
            now = time.monotonic()
            if now >= next_status:
                next_status = now + 0.1
                control_path = directory / "proxy-control.json"
                if control_path.exists():
                    control = json.loads(control_path.read_text(encoding="utf-8-sig"))
                    if control.get("id") != control_id:
                        control_id = control.get("id")
                        if control.get("stop"):
                            break
                        if "upDelayMs" in control:
                            up_ms = max(0, min(14000, int(control["upDelayMs"])))
                        if control.get("resetClient"):
                            client = None
                            for _, _, direction, _ in queue:
                                counters[direction]["dropped"] += 1
                            queue.clear()
                            next_wire = {"up": now, "down": now}
                        blackout_ms = max(0, min(15000, int(control.get("blackoutMs", 0))))
                        blackout_until = now + blackout_ms / 1000
                        down_blackout_ms = max(0, min(15000, int(control.get("downBlackoutMs", 0))))
                        down_blackout_until = now + down_blackout_ms / 1000
                        down_blackout_end_ms = round(time.time() * 1000) + down_blackout_ms
                        if down_blackout_ms:
                            counters["down"]["dropped"] += sum(1 for item in queue if item[2] == "down")
                            queue = [item for item in queue if item[2] != "down"]
                            heapq.heapify(queue)
                            next_wire["down"] = now
                        # Anything already in flight is lost at the start of the outage too.
                        if blackout_ms:
                            for _, _, direction, _ in queue:
                                counters[direction]["dropped"] += 1
                            queue.clear()
                            next_wire = {"up": now, "down": now}
                atomic(directory / "proxy-status.json", dict(elapsed=now-started,
                       counters=counters, queued=len(queue), maximumQueue=maximum_queue,
                       blackout=now < blackout_until, appliedControl=control_id, upDelayMs=up_ms,
                       downBlackout=now < down_blackout_until, downBlackoutEndMs=down_blackout_end_ms))
            while queue and queue[0][0] <= now:
                _, _, direction, data = heapq.heappop(queue)
                if now < blackout_until or (direction == "down" and now < down_blackout_until):
                    counters[direction]["dropped"] += 1
                    continue
                try:
                    if direction == "up":
                        back.send(data)
                    elif client:
                        front.sendto(data, client)
                    counters[direction]["sent"] += 1
                    counters[direction]["bytes"] += len(data)
                except (ConnectionResetError, ConnectionRefusedError):
                    counters[direction]["dropped"] += 1
            timeout = min(0.01, max(0, queue[0][0] - now)) if queue else 0.01
            for key, _ in selector.select(timeout):
                direction = key.data
                try:
                    data, address = key.fileobj.recvfrom(65535)
                except (BlockingIOError, ConnectionResetError, ConnectionRefusedError):
                    continue
                if direction == "up":
                    if client is not None and client != address:
                        continue  # exactly one guest, never an open relay
                    client = address
                counters[direction]["received"] += 1
                if now < blackout_until or (direction == "down" and now < down_blackout_until) or rng.random() * 100 < loss or len(queue) >= 16384:
                    counters[direction]["dropped"] += 1
                    continue
                copies = 2 if rng.random() * 100 < duplicate else 1
                counters[direction]["duplicated"] += copies - 1
                for _ in range(copies):
                    queued_at = time.monotonic()
                    if rates[direction]:
                        if next_wire[direction] - queued_at > 2:
                            counters[direction]["dropped"] += 1
                            continue
                        next_wire[direction] = max(queued_at, next_wire[direction]) + len(data) / rates[direction]
                        queued_at = next_wire[direction]
                    delay = max(0, (up_ms if direction == "up" else down_ms) + rng.uniform(-jitter_ms, jitter_ms))
                    serial += 1
                    heapq.heappush(queue, (queued_at + delay / 1000, serial, direction, data))
                maximum_queue = max(maximum_queue, len(queue))
    atomic(directory / "proxy-final.json", dict(profile=args.profile, seed=args.seed, bytesPerSecond=rates,
           counters=counters, queued=len(queue), maximumQueue=maximum_queue, elapsed=time.monotonic()-started))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-directory", required=True)
    parser.add_argument("--server-port", type=int, required=True)
    parser.add_argument("--profile", choices=PROFILES, required=True)
    parser.add_argument("--seed", type=int, default=119)
    parser.add_argument("--seconds", type=int, default=900)
    run(parser.parse_args())
