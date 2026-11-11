#!/usr/bin/env python3
"""Check the event log of one `--self-exercise` run of the desktop automation fixture.

The self-exercise activates every control in a fixed order, so the log it
leaves is known in advance. A mismatch means the event log contract in
README.md no longer describes what the fixture writes.
"""

from __future__ import annotations

import json
import sys
from datetime import datetime
from pathlib import Path


BUNDLE_IDENTIFIER = "dev.asura-fixture.desktop-automation"
FORMAT_VERSION = 1
# The text the self-exercise puts in the secure field. It must never be logged.
EXERCISED_SECRET = "s3cret-fixture-value"

# Activations per control, in the order they are made. The disabled button is
# clicked as well and must not appear.
EXPECTED_ACTIONS = [
    "fixture.checkbox",
    "fixture.popup",
    "fixture.disclosure",
    "fixture.menu.record",
    "fixture.menu.variant-2",
    "fixture.action-button",
    "fixture.action-button",
    "fixture.twin-button",
    "fixture.twin-button",
    "fixture.stale-target",
    "fixture.text-field-2",
    "fixture.sheet-button",
    "fixture.sheet.dont-save",
    "fixture.alert-button",
    "fixture.alert.ok",
    "fixture.slow-button",
]

EXPECTED_VALUES = {
    "fixture.window": [True, False, True],
    "fixture.text-field-1": ["", "alpha", ""],
    "fixture.text-field-2": ["", "beta", "x", ""],
    "fixture.checkbox": [False, True, False],
    "fixture.popup": ["Vanilla", "Strawberry", "Vanilla"],
    "fixture.disclosure": [False, True, False],
    "fixture.table": [None, "Row 40", None],
    "fixture.stale-target": [True, False, True],
    "fixture.sheet": [False, True, False, True, False],
    "fixture.alert": [False, True, False, True, False],
}

PRESS = ["AXPress"]
EDIT = ["AXConfirm", "AXShowMenu"]
PICK = ["AXCancel", "AXPick", "AXPress"]
SCROLL = ["AXScrollDownByPage", "AXScrollLeftByPage", "AXScrollRightByPage", "AXScrollUpByPage"]
TITLE = "Desktop Automation Fixture"

# What an accessibility client is told about each identified element:
# (identifier, depth below its window or menu bar item, role, subrole, title,
# description, action names). README.md repeats this table for test authors.
WINDOW_INVENTORY = [
    ("fixture.window", 0, "AXWindow", "AXStandardWindow", TITLE, None, ["AXRaise"]),
    ("fixture.text-field-1", 1, "AXTextField", None, None, "First Text", EDIT),
    ("fixture.text-field-2", 1, "AXTextField", None, None, "Second Text", EDIT),
    ("fixture.secure-field", 1, "AXTextField", "AXSecureTextField", None, "Secret", EDIT),
    ("fixture.checkbox", 1, "AXCheckBox", None, "Enable Option", None, PRESS),
    ("fixture.popup", 1, "AXPopUpButton", None, None, "Flavor", ["AXPress", "AXShowMenu"]),
    ("fixture.disclosure", 1, "AXDisclosureTriangle", None, None, "Details", PRESS),
    ("fixture.scroll-view", 1, "AXScrollArea", None, None, "Rows", SCROLL),
    ("fixture.table", 2, "AXTable", None, None, "Rows", []),
    ("fixture.action-button", 1, "AXButton", None, "Record Action", None, PRESS),
    ("fixture.slow-button", 1, "AXButton", None, "Slow Action", None, PRESS),
    ("fixture.disabled-button", 1, "AXButton", None, "Disabled Action", None, PRESS),
    ("fixture.sheet-button", 1, "AXButton", None, "Open Unsaved Changes Sheet", None, PRESS),
    ("fixture.alert-button", 1, "AXButton", None, "Show Modal Alert", None, PRESS),
    ("fixture.twin-button", 1, "AXButton", None, "Twin Action", None, PRESS),
    ("fixture.twin-button", 1, "AXButton", None, "Twin Action", None, PRESS),
    ("fixture.stale-target", 1, "AXButton", None, "Stale Target", None, PRESS),
    ("fixture.status", 1, "AXStaticText", None, None, "Status", []),
]
MENU_INVENTORY = [
    ("fixture.menu.application", 0, "AXMenuBarItem", None, TITLE, None, PICK),
    ("fixture.menu.quit", 2, "AXMenuItem", None, "Quit " + TITLE, None, PICK),
    ("fixture.menu.edit", 0, "AXMenuBarItem", None, "Edit", None, PICK),
    ("fixture.menu", 0, "AXMenuBarItem", None, "Fixture", None, PICK),
    ("fixture.menu.record", 2, "AXMenuItem", None, "Record Menu Action", None, PICK),
    ("fixture.menu.variants", 2, "AXMenuItem", None, "Variants", None, PICK),
    ("fixture.menu.variant-1", 4, "AXMenuItem", None, "Variant One", None, PICK),
    ("fixture.menu.variant-2", 4, "AXMenuItem", None, "Variant Two", None, PICK),
    ("fixture.menu.variant-3", 4, "AXMenuItem", None, "Variant Three", None, PICK),
]
# The sheet is a child of the window it is attached to; the alert is a window
# of its own.
SHEET_INVENTORY = [
    ("fixture.sheet", 1, "AXSheet", None, None, "alert", ["AXRaise"]),
    ("fixture.sheet.save", 2, "AXButton", None, "Save", None, PRESS),
    ("fixture.sheet.cancel", 2, "AXButton", None, "Cancel", None, PRESS),
    ("fixture.sheet.dont-save", 2, "AXButton", None, "Don't Save", None, PRESS),
]
ALERT_INVENTORY = [
    ("fixture.alert", 0, "AXWindow", "AXDialog", "Fixture Alert", "alert", ["AXRaise"]),
    ("fixture.alert.ok", 1, "AXButton", None, "OK", None, PRESS),
    ("fixture.alert.cancel", 1, "AXButton", None, "Cancel", None, PRESS),
]
# Per stage: the extra elements, the application's focused window, and the
# elements that report themselves modal.
EXPECTED_STAGES = {
    "launch": ([], "fixture.window", []),
    "expanded": (
        [("fixture.disclosure-content", 1, "AXStaticText", None, None, None, [])],
        "fixture.window",
        [],
    ),
    "sheet": (SHEET_INVENTORY, "fixture.sheet", []),
    "alert": (ALERT_INVENTORY, "fixture.alert", ["fixture.alert"]),
}

EXPECTED_COMMANDS = [
    ("remove-stale-target", True),
    ("not-a-command", False),
    ("reset", True),
    ("show-sheet", True),
    ("reset", True),
    ("show-alert", True),
    ("reset", True),
    ("close-window", True),
    ("reset", True),
    ("stall", True),
]


def check(log_path: Path) -> list[str]:
    raw = log_path.read_text(encoding="utf-8")
    errors: list[str] = []
    if EXERCISED_SECRET in raw:
        errors.append("the secure field's text reached the event log")

    events = []
    for number, line in enumerate(raw.splitlines(), start=1):
        try:
            event = json.loads(line)
        except json.JSONDecodeError as error:
            return [f"line {number} is not JSON: {error}"]
        if not isinstance(event, dict):
            return [f"line {number} is not a JSON object"]
        events.append(event)
    if not events:
        return ["the event log is empty"]

    process = events[0].get("pid")
    for number, event in enumerate(events, start=1):
        if not isinstance(event.get("event"), str):
            errors.append(f"line {number} has no event name")
        if event.get("pid") != process or not isinstance(process, int):
            errors.append(f"line {number} is not from process {process}")
        if event.get("seq") != number:
            errors.append(f"line {number} carries seq {event.get('seq')}")
        try:
            datetime.strptime(str(event.get("time")), "%Y-%m-%dT%H:%M:%S.%fZ")
        except ValueError:
            errors.append(f"line {number} has no UTC millisecond timestamp")
    if errors:
        return errors

    def named(name: str) -> list[dict]:
        return [event for event in events if event["event"] == name]

    launched = events[0]
    if (
        launched["event"] != "launched"
        or launched.get("bundleIdentifier") != BUNDLE_IDENTIFIER
        or launched.get("formatVersion") != FORMAT_VERSION
        or launched.get("selfExercise") is not True
    ):
        errors.append(f"unexpected first event: {launched}")
    if [event.get("windowVisible") for event in named("ready")] != [True]:
        errors.append("the window was not reported visible exactly once")
    if events[-1]["event"] != "exiting" or events[-1].get("reason") != "self-exercise-complete":
        errors.append(f"unexpected last event: {events[-1]}")

    actions = named("action")
    if [event.get("control") for event in actions] != EXPECTED_ACTIONS:
        errors.append(f"unexpected activations: {[event.get('control') for event in actions]}")
    counts: dict[str, int] = {}
    for event in actions:
        control = event.get("control")
        counts[control] = counts.get(control, 0) + 1
        if event.get("count") != counts[control]:
            errors.append(f"{control} activation {counts[control]} carries count {event.get('count')}")
    twins = [event.get("instance") for event in actions if event.get("control") == "fixture.twin-button"]
    if twins != ["first", "second"]:
        errors.append(f"the twin buttons were not told apart: {twins}")

    states = named("state")
    for control, expected in EXPECTED_VALUES.items():
        values = [event.get("value") for event in states if event.get("control") == control]
        if values != expected:
            errors.append(f"{control} went through {values}, expected {expected}")
    lengths = [event.get("length") for event in states if event.get("control") == "fixture.secure-field"]
    if lengths != [0, len(EXERCISED_SECRET), 1, 0]:
        errors.append(f"the secure field went through lengths {lengths}")
    if any("value" in event for event in states if event.get("control") == "fixture.secure-field"):
        errors.append("a secure field state carries a value")
    scrolled = [
        (event.get("firstVisibleRow"), event.get("lastVisibleRow"))
        for event in states
        if event.get("control") == "fixture.scroll-view"
    ]
    if (
        len(scrolled) != 3
        or scrolled[0][0] != 1
        or scrolled[1][1] != 40
        or scrolled[2] != scrolled[0]
        or not scrolled[0][1] < scrolled[1][0]
    ):
        errors.append(f"the scroll view went through {scrolled}")

    keys = named("key")
    plain = {"characters": "x", "focus": "fixture.text-field-2", "keyCode": 7, "modifiers": []}
    confirm = {"characters": "\r", "focus": "fixture.text-field-2", "keyCode": 36, "modifiers": []}
    redacted = {"focus": "fixture.secure-field", "redacted": True}
    shown = [
        {name: value for name, value in event.items() if name not in ("event", "pid", "seq", "time")}
        for event in keys
    ]
    if shown != [plain, confirm, redacted]:
        errors.append(f"unexpected key events: {shown}")

    commands = [(event.get("command"), event.get("accepted")) for event in named("control")]
    if commands != EXPECTED_COMMANDS:
        errors.append(f"unexpected control commands: {commands}")
    if [event.get("sheetAttached") for event in named("quit-requested")] != [True]:
        errors.append("the quit request under the sheet was not recorded exactly once")
    stalls = [event.get("command") or event.get("control") for event in named("stall-ended")]
    if stalls != ["stall", "fixture.slow-button"]:
        errors.append(f"unexpected stalls: {stalls}")

    errors.extend(check_inventory(named("inventory")))

    known = {
        "launched", "ready", "state", "action", "key", "control", "quit-requested", "stall-ended",
        "inventory", "exiting",
    }
    unknown = sorted({event["event"] for event in events} - known)
    if unknown:
        errors.append(f"undocumented events: {unknown}")
    return errors


def check_inventory(inventory: list[dict]) -> list[str]:
    errors: list[str] = []
    stages = sorted({event.get("stage") for event in inventory}, key=str)
    if stages != sorted(EXPECTED_STAGES):
        return [f"the inventory covers stages {stages}"]
    for stage, (extra, focused, modal) in EXPECTED_STAGES.items():
        staged = [event for event in inventory if event["stage"] == stage]
        rows = [event for event in staged if str(event.get("control")).startswith("fixture.row-")]
        seen = [
            (
                event.get("control"), event.get("depth"), event.get("role"), event.get("subrole"),
                event.get("title"), event.get("description"), event.get("actions"),
            )
            for event in staged
            if event not in rows
        ]
        expected = WINDOW_INVENTORY + MENU_INVENTORY + extra
        if sorted(seen, key=repr) != sorted(expected, key=repr):
            missing = [entry for entry in expected if entry not in seen]
            surplus = [entry for entry in seen if entry not in expected]
            errors.append(f"{stage} inventory differs: missing {missing}, unexpected {surplus}")
        # Only the rows scrolled into view have a text element to identify.
        row_names = [event.get("control") for event in rows]
        if len(rows) < 3 or row_names != [f"fixture.row-{number}" for number in range(1, len(rows) + 1)]:
            errors.append(f"{stage} inventory shows rows {row_names}")
        if any((event.get("role"), event.get("depth")) != ("AXStaticText", 5) for event in rows):
            errors.append(f"{stage} inventory rows are not static text inside table cells")
        if [event.get("control") for event in staged if event.get("focusedWindow") is True] != [focused]:
            errors.append(f"{stage} inventory does not show {focused} as the focused window")
        if [event.get("control") for event in staged if event.get("modal") is True] != modal:
            errors.append(f"{stage} inventory does not show exactly {modal} as modal")
    return errors


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: check_self_exercise.py <events.jsonl>", file=sys.stderr)
        return 64
    errors = check(Path(sys.argv[1]))
    if errors:
        for error in errors:
            print(f"desktop-automation self-exercise failed: {error}", file=sys.stderr)
        return 1
    print("desktop-automation self-exercise log passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
