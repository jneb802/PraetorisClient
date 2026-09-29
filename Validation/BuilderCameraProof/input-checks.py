#!/usr/bin/env python3
"""Run on a leased Valnet desktop with the proof character on a stable platform.

Requires the proof plugin, developer commands, an equipped belt and hammer,
a fueled ward, and a clear site. Uses real X11 keyboard input. Writes JSON
evidence to stdout. The caller must restore the backed-up test world afterward.
"""
import json
import math
import os
import re
import subprocess
import time

os.environ.update(DISPLAY=":0", XAUTHORITY="/run/user/1000/gdm/Xauthority")


def cli(*args):
    result = subprocess.run([os.path.expanduser("~/.local/bin/valheim-cli"), *map(str, args)], capture_output=True, text=True, check=True)
    if "ERROR:" in result.stdout:
        raise RuntimeError(result.stdout)
    return result.stdout


def vector(text, field):
    match = re.search(re.escape(field) + r"=\(([^)]+)\)", text)
    return tuple(map(float, match[1].split(",")))


def state():
    text = cli("buildercamera", "status")
    return {"active": "active=True" in text, "camera": vector(text, "camera"), "body": vector(text, "body")}


def hold(key, seconds):
    subprocess.run(["xdotool", "keydown", key], check=True)
    try:
        time.sleep(seconds)
    finally:
        subprocess.run(["xdotool", "keyup", key], check=True)


def start():
    if state()["active"]:
        cli("buildercamera")
    time.sleep(1.1)
    cli("buildercamera")
    time.sleep(0.2)
    result = state()
    assert result["active"], result
    return result


def stop():
    if state()["active"]:
        cli("buildercamera")


def near(a, b, tolerance=0.06):
    return math.dist(a, b) <= tolerance


def aim(point):
    cli("builderproof", "aim", *point)


subprocess.run(["wmctrl", "-a", "Valheim"], check=True)
cli("cli_build_select", "piece_repair")
results = {}
try:
    # Keep the player stationary while attempting to cross a solid stone wall.
    before = start()
    x, y, z = before["camera"]
    cli("cli_spawn_at", "stone_wall_4x2", x, y - 1, z + 3)
    aim((x, y, z + 10))
    hold("w", 2)
    blocked = state()
    hold("w", 1)
    repeated = state()
    assert repeated["active"] and near(blocked["camera"], repeated["camera"]), (blocked, repeated)
    assert z < repeated["camera"][2] < z + 3, (before, repeated)
    assert near(before["body"], repeated["body"]), (before, repeated)
    results["wall"] = {"before": before, "blocked": blocked, "repeated": repeated, "wallZ": z + 3}

    # Move sideways out of the wall's width, then try to descend through terrain.
    hold("a", 1)
    hold("space", 1.5)
    raised = state()
    hold("Control_L", 4)
    blocked = state()
    hold("Control_L", 1)
    repeated = state()
    assert repeated["active"] and near(blocked["camera"], repeated["camera"]), (blocked, repeated)
    assert raised["camera"][1] - repeated["camera"][1] > 2, (raised, repeated)
    assert near(before["body"], repeated["body"]), (before, repeated)
    aim((repeated["camera"][0], -100, repeated["camera"][2]))
    ray = cli("cli_build_status")
    assert "heightmap=True" in ray, ray
    results["terrain"] = {"raised": raised, "blocked": blocked, "repeated": repeated, "ray": ray}
    stop()
finally:
    stop()
    print(json.dumps(results, indent=2), flush=True)
