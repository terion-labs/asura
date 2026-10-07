#!/usr/bin/env python3
"""Lint user-visible strings in src/ against docs/ui-writing-guide.md.

What is read:

* AXAML: the values of user-facing attributes (Content, Header, Title, Text,
  PlaceholderText, Watermark, ToolTip.Tip, AutomationProperties.Name and
  .HelpText, and the label/description attributes of Asura's own controls),
  plus text written as element content of text elements. Bindings and other
  markup extensions are skipped.
* C#: string literals assigned to, or passed as a named argument for, a
  user-facing member (Title, Header, Label, Text, Status, Message, ...);
  arguments of the shell's status and error helpers (Fail, Warn, Report...,
  ToolTip.SetTip, ...); and any other literal that reads as prose (starts with
  a capital letter and has a second word). Logging calls and attribute
  arguments are never read; lowercase code values ("cancelled") are skipped.

Rules: spelling (en-US), ellipsis ("..." instead of "…"), secret-store
(hand-written names for the OS secret store; use Asura.Core.PlatformTerms),
preferences ("Preferences" instead of "Settings"), plural ("item(s)"), and
title-case for button labels, menu items, tab items, segment labels, window
titles, and their C# equivalents.

True exceptions go in scripts/ui-copy-allowlist.txt, one per line:

    rule | path glob | text | reason

"rule" may be "*". "text" is compared with the flagged fragment (the word or
phrase the finding names) and with the whole string. Every entry needs a
reason. The script exits 1 when anything is left.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import fnmatch
import os
from pathlib import Path
import re
import sys
import xml.parsers.expat


REPOSITORY = Path(__file__).resolve().parent.parent
DEFAULT_ALLOWLIST = REPOSITORY / "scripts" / "ui-copy-allowlist.txt"
SKIPPED_DIRECTORIES = {"bin", "obj", ".git"}


@dataclass(frozen=True)
class UiString:
    path: str
    line: int
    text: str
    context: str
    title_case: bool = False


@dataclass(frozen=True)
class Finding:
    path: str
    line: int
    rule: str
    fragment: str
    message: str
    text: str
    context: str

    def render(self) -> str:
        shown = " ".join(self.text.split())
        if len(shown) > 120:
            shown = shown[:117] + "..."
        return f"{self.path}:{self.line}: [{self.rule}] {self.message} ({self.context}): {shown!r}"


# --- Rules ------------------------------------------------------------------

def _ise(stem: str) -> str:
    return stem + r"(?:e|ed|es|er|ers|ing|ation|ations)"


BRITISH_SPELLINGS = re.compile(
    r"\b(?:"
    + "|".join(
        [
            r"colour(?:s|ed|ing|ful|less)?",
            r"cancell(?:ed|ing)",
            r"licence(?:s|d)?",
            r"behaviour(?:s|al)?",
            r"favourite(?:s|d)?",
            r"honour(?:s|ed|ing)?",
            r"flavour(?:s|ed)?",
            r"neighbour(?:s|ing|hood)?",
            r"centre(?:s|d)?",
            r"theatre(?:s)?",
            r"fibre(?:s)?",
            r"analys(?:e|ed|ing)",
            r"catalogue(?:s|d)?",
            r"dialogue(?:s)?",
            r"programme(?:s)?",
            r"defence(?:s)?",
            r"judgement(?:s)?",
            r"acknowledgement(?:s)?",
            r"grey(?:ed|s|ing)?",
            r"label(?:led|ling)",
            r"model(?:led|ling)",
            r"travel(?:led|ling)",
            r"signal(?:led|ling)",
            r"fulfil(?:s|ment)?",
            r"enrol(?:s|ment)?",
            r"artefact(?:s)?",
            r"whilst",
            r"amongst",
        ]
        + [
            _ise(stem)
            for stem in [
                "recognis", "organis", "initialis", "authoris", "customis", "optimis",
                "prioritis", "serialis", "deserialis", "synchronis", "normalis",
                "minimis", "maximis", "visualis", "summaris", "categoris", "finalis",
                "standardis", "utilis", "realis", "specialis", "personalis",
                "capitalis", "localis", "sanitis", "tokenis", "memoris", "virtualis",
                "containeris", "parameteris", "parametris", "materialis",
            ]
        ]
    )
    + r")\b",
    re.IGNORECASE,
)

THREE_PERIODS = re.compile(r"(?<!\.)\.\.\.(?!\.)")

SECRET_STORE_PHRASES = re.compile(
    r"\b(?:"
    r"OS[ -](?:level )?(?:vault|keychain|keystore|key store|credential store|secret store)"
    r"|operating[- ]system (?:vault|keychain|keystore|key store|credential store|secret store)"
    r"|system (?:vault|keychain|keystore|key store|credential store|secret store)"
    r"|platform (?:vault|keychain|keystore|credential store|secret store)"
    r"|macOS Keychain"
    r")\b",
    re.IGNORECASE,
)

PREFERENCES = re.compile(r"\bPreferences\b")

# "HTTP(S)" names two protocols; only a lowercase word with "(s)" is a plural.
PLURAL_HACK = re.compile(r"\b\w*[a-z]\w*\((?:s|es)\)")

# Apple title style: capitalize every word except articles, coordinating
# conjunctions, and prepositions of four or fewer letters, unless the word is
# first or last.
MINOR_WORDS = {
    "a", "an", "the",
    "and", "but", "or", "nor", "for", "so", "yet",
    "as", "at", "by", "down", "for", "from", "in", "into", "like", "near", "of",
    "off", "on", "onto", "out", "over", "past", "per", "than", "till", "to",
    "up", "upon", "via", "with", "vs",
}
# Minor words that are also verb particles ("Log In", "Zoom Out", "Set Up")
# may be capitalized mid-title; the rest must be lowercase there.
PARTICLES = {"down", "in", "off", "on", "out", "over", "up"}
HYPHEN_PREFIXES = {"anti", "auto", "co", "de", "multi", "non", "pre", "re", "semi", "sub", "un"}
TOKEN_EDGE = "\"“”‘’'()[]<>,;!?…*"
CODE_CHARACTERS = re.compile(r"[./\\_@=#$%&+<>|~^:{}0-9]")
HOLE = "⁣"  # Marks an interpolation hole; never checked as a word.


def title_case_problems(text: str) -> list[tuple[str, str]]:
    """Return (word, message) for each word that breaks title style."""
    words: list[tuple[str, bool]] = []  # (word, starts a title segment); "" for quoted text
    starts_segment = True
    quoted = False
    for raw in text.split():
        if raw in {"—", "–", "-", ":", "·", "|", "/"}:
            starts_segment = True
            continue
        bare = raw.rstrip(".,:;!?…")
        opens = raw[0] in "“\"‘"
        closes = bare.endswith(("”", "\"", "’")) and (len(bare) > 1 or not opens)
        if quoted or opens:
            # Quoted names are written as their owners write them.
            quoted = not closes
            words.append(("", starts_segment))
            starts_segment = False
            continue
        words.append((raw.strip(TOKEN_EDGE).rstrip("."), starts_segment))
        starts_segment = raw.endswith((":", "—"))

    problems = []
    last_index = len(words) - 1
    for index, (token, segment_start) in enumerate(words):
        if not token or HOLE in token or not any(c.isalpha() for c in token):
            continue
        if CODE_CHARACTERS.search(token):
            continue
        if token[0].islower() and any(c.isupper() for c in token[1:]):
            continue  # iOS, macOS, eBPF
        parts = re.split(r"[-–]", token)
        edge = segment_start or index == last_index
        problem = _word_problem(parts[0], edge, index == last_index)
        if problem:
            problems.append((token, problem))
            continue
        if parts[0].lower() in HYPHEN_PREFIXES:
            continue
        for part in parts[1:]:
            if part and part[0].islower() and part.lower() not in MINOR_WORDS:
                problems.append((token, f"capitalize “{part}” in “{token}”"))
                break
    return problems


def _word_problem(word: str, edge: bool, last: bool) -> str | None:
    if not word or not word[0].isalpha():
        return None
    lower = word.lower()
    if edge:
        if word[0].islower():
            return f"capitalize “{word}”" + (" (last word)" if last else "")
        return None
    if lower in MINOR_WORDS:
        if word[0].isupper() and lower not in PARTICLES and word != word.upper():
            return f"lowercase “{word}”"
        return None
    if word[0].islower():
        return f"capitalize “{word}”"
    return None


def check(item: UiString) -> list[Finding]:
    text = item.text
    visible = text.replace(HOLE, "{}")
    found: list[Finding] = []

    def add(rule: str, fragment: str, message: str) -> None:
        found.append(Finding(item.path, item.line, rule, fragment, message, visible, item.context))

    for match in BRITISH_SPELLINGS.finditer(visible):
        word = match.group(0)
        add("spelling", word, f"use en-US spelling instead of “{word}”")
    if THREE_PERIODS.search(visible):
        add("ellipsis", "...", "use the “…” character instead of three periods")
    for match in SECRET_STORE_PHRASES.finditer(visible):
        add(
            "secret-store",
            match.group(0),
            f"name the secret store through PlatformTerms instead of “{match.group(0)}”",
        )
    for match in PREFERENCES.finditer(visible):
        add("preferences", match.group(0), "say “Settings”, not “Preferences”")
    for match in PLURAL_HACK.finditer(visible):
        add("plural", match.group(0), f"write a real plural instead of “{match.group(0)}”")
    if item.title_case:
        for word, message in title_case_problems(text):
            add("title-case", word, message)
    return found


# --- AXAML ------------------------------------------------------------------

XAML_ATTRIBUTES = {
    "Content", "Header", "Title", "Text", "PlaceholderText", "Watermark", "ToolTip.Tip",
    "AutomationProperties.Name", "AutomationProperties.HelpText",
    # Asura's own controls.
    "Label", "Description", "Heading", "Body", "Hint", "Subtitle", "Detail", "Caption",
    "Message", "Placeholder", "Footnote", "FooterHint", "AutomationName", "ActionLabel",
    "CloseLabel", "ClearLabel", "ConfirmLabel", "CancelLabel", "OnLabel", "OffLabel",
    "OnContent", "OffContent",
}
XAML_TEXT_ELEMENTS = {
    "TextBlock", "SelectableTextBlock", "Run", "Span", "Bold", "Italic", "Underline",
    "Label", "ToolTip", "String", "Button", "ToggleButton", "SplitButton",
    "DropDownButton", "RepeatButton", "ToggleSplitButton", "CheckBox", "RadioButton",
    "MenuItem", "TabItem",
}
XAML_BUTTONS = {"Button", "ToggleButton", "SplitButton", "DropDownButton", "RepeatButton", "ToggleSplitButton"}
XAML_SEGMENT_HOSTS = {"SegmentedControl"}


def _local(name: str) -> str:
    return name.rsplit(":", 1)[-1]


def _classes(attributes: dict[str, str]) -> set[str]:
    return set(re.split(r"[\s,]+", attributes.get("Classes", "").strip())) - {""}


def _xaml_context(element: dict, attribute: str, stack: list[dict]) -> tuple[str, bool]:
    local = element["local"]
    label = f"{local} {attribute}"
    if attribute == "Content":
        in_segment = "Segment" in _classes(element["attributes"]) or any(
            ancestor["local"] in XAML_SEGMENT_HOSTS
            or (ancestor["local"] == "Border" and "Segmented" in _classes(ancestor["attributes"]))
            for ancestor in stack[-3:-1]
        )
        if local in {"RadioButton", "ToggleButton"} and in_segment:
            return f"segment label ({local})", True
        if local in XAML_BUTTONS:
            return f"button label ({local})", True
    if attribute == "Header" and local in {"MenuItem", "NativeMenuItem", "TabItem"}:
        return f"{local} header", True
    if attribute == "Title" and (local == "DialogShell" or (len(stack) == 1 and local.endswith("Window"))):
        return f"window title ({local})", True
    return label, False


def _xaml_value(value: str) -> str | None:
    if value.startswith("{}"):
        return value[2:]
    if value.startswith("{"):
        return None
    return value


def axaml_strings(path: str, source: bytes) -> list[UiString]:
    parser = xml.parsers.expat.ParserCreate()
    parser.buffer_text = True
    stack: list[dict] = []
    strings: list[UiString] = []
    line_starts = [0] + [m.end() for m in re.finditer(b"\n", source)]

    def line_of(offset: int) -> int:
        low, high = 0, len(line_starts)
        while low + 1 < high:
            middle = (low + high) // 2
            if line_starts[middle] <= offset:
                low = middle
            else:
                high = middle
        return low + 1

    def emit(owner: dict, attribute: str, text: str, line: int, owner_stack: list[dict]) -> None:
        if not text.strip():
            return
        context, title_case = _xaml_context(owner, attribute, owner_stack)
        strings.append(UiString(path, line, text, context, title_case))

    def start(name: str, attributes: dict[str, str]) -> None:
        offset = parser.CurrentByteIndex
        local = _local(name)
        element = {"name": name, "local": local, "attributes": attributes}
        stack.append(element)
        if "." in name and len(stack) > 1:
            return  # A property element; its text belongs to the owner.
        for attribute, raw in attributes.items():
            if attribute not in XAML_ATTRIBUTES:
                continue
            value = _xaml_value(raw)
            if value is None:
                continue
            match = re.compile(rb"\s" + re.escape(attribute.encode()) + rb"\s*=").search(source, offset)
            emit(element, attribute, value, line_of(match.start() + 1) if match else line_of(offset), stack)

    def end(name: str) -> None:
        stack.pop()

    def text(data: str) -> None:
        if not stack or not data.strip():
            return
        element = stack[-1]
        line = parser.CurrentLineNumber + data[: len(data) - len(data.lstrip())].count("\n")
        if "." in element["name"]:
            attribute = element["name"].rpartition(".")[2]
            if attribute in XAML_ATTRIBUTES and len(stack) > 1:
                emit(stack[-2], attribute, data.strip(), line, stack[:-1])
            return
        if element["local"] in XAML_TEXT_ELEMENTS:
            attribute = "Content" if element["local"] in XAML_BUTTONS | {"CheckBox", "RadioButton"} else "Text"
            if element["local"] in {"MenuItem", "TabItem"}:
                attribute = "Header"
            emit(element, attribute, data.strip(), line, stack)

    parser.StartElementHandler = start
    parser.EndElementHandler = end
    parser.CharacterDataHandler = text
    try:
        parser.Parse(source, True)
    except xml.parsers.expat.ExpatError as error:
        raise SystemExit(f"{path}: cannot parse AXAML: {error}") from error
    return strings


# --- C# ---------------------------------------------------------------------

@dataclass
class Literal:
    start: int
    end: int
    value: str
    parent: int | None = None


_ESCAPES = {"n": "\n", "t": "\t", "r": "\r", "0": "\0", "\\": "\\", "\"": "\"", "'": "'", "a": "", "b": "", "f": "", "v": "", "e": ""}


def _blank(text: str) -> str:
    return "".join("\n" if c == "\n" else " " for c in text)


def scan_csharp(source: str) -> tuple[list[Literal], str]:
    """Find string literals, and return the source with comments and literal contents blanked."""
    literals: list[Literal] = []
    out = list(source)
    length = len(source)

    def blank(start: int, end: int, keep_quotes: bool) -> None:
        for index in range(start, end):
            if out[index] != "\n":
                out[index] = " "
        if keep_quotes and end - start >= 2:
            out[start] = "\""
            out[end - 1] = "\""

    def char_literal(i: int) -> int:
        j = i + 1
        while j < length and source[j] != "'":
            j += 2 if source[j] == "\\" else 1
        return min(j + 1, length)

    def string_literal(i: int, parent: int | None) -> int:
        j = i
        dollars = 0
        verbatim = False
        while source[j] in "$@":
            if source[j] == "$":
                dollars += 1
            else:
                verbatim = True
            j += 1
        quotes = 0
        while j + quotes < length and source[j + quotes] == "\"":
            quotes += 1
        index = len(literals)
        literals.append(Literal(i, i, "", parent))
        if quotes >= 3:
            content_start = j + quotes
            close = source.find("\"" * quotes, content_start)
            close = length if close < 0 else close
            body = source[content_start:close]
            if dollars:
                braces = "{" * dollars
                body = re.sub(re.escape(braces) + r".*?" + re.escape("}" * dollars), HOLE, body, flags=re.S)
            end = min(close + quotes, length)
            literals[index].value = body
            literals[index].end = end
            return end
        if quotes == 2 and not dollars and not verbatim:
            literals[index].end = j + 2
            return j + 2
        j += 1
        value: list[str] = []
        while j < length:
            c = source[j]
            if verbatim and c == "\"":
                if j + 1 < length and source[j + 1] == "\"":
                    value.append("\"")
                    j += 2
                    continue
                j += 1
                break
            if not verbatim and c == "\"":
                j += 1
                break
            if not verbatim and c == "\\" and j + 1 < length:
                nxt = source[j + 1]
                if nxt == "u" and re.fullmatch(r"[0-9A-Fa-f]{4}", source[j + 2:j + 6]):
                    value.append(chr(int(source[j + 2:j + 6], 16)))
                    j += 6
                    continue
                value.append(_ESCAPES.get(nxt, nxt))
                j += 2
                continue
            if not verbatim and c == "\n":
                break  # Unterminated; give up on this literal.
            if dollars and c == "{":
                if j + 1 < length and source[j + 1] == "{":
                    value.append("{")
                    j += 2
                    continue
                j = hole(j + 1, index)
                value.append(HOLE)
                continue
            if dollars and c == "}" and j + 1 < length and source[j + 1] == "}":
                value.append("}")
                j += 2
                continue
            value.append(c)
            j += 1
        literals[index].value = "".join(value)
        literals[index].end = j
        return j

    def hole(j: int, parent: int) -> int:
        depth = 0
        while j < length:
            c = source[j]
            if c in "([{":
                depth += 1
            elif c in ")]":
                depth -= 1
            elif c == "}":
                if depth == 0:
                    return j + 1
                depth -= 1
            elif c == "'":
                j = char_literal(j)
                continue
            elif c == "\"" or (c in "$@" and _string_start(source, j)):
                j = string_literal(j, parent)
                continue
            j += 1
        return j

    i = 0
    while i < length:
        c = source[i]
        if c == "/" and i + 1 < length and source[i + 1] == "/":
            end = source.find("\n", i)
            end = length if end < 0 else end
            blank(i, end, False)
            i = end
        elif c == "/" and i + 1 < length and source[i + 1] == "*":
            end = source.find("*/", i + 2)
            end = length if end < 0 else end + 2
            blank(i, end, False)
            i = end
        elif c == "'":
            end = char_literal(i)
            blank(i, end, False)
            i = end
        elif c == "\"" or (c in "$@" and _string_start(source, i)):
            end = string_literal(i, None)
            blank(i, end, True)
            i = end
        else:
            i += 1
    return literals, "".join(out)


def _string_start(source: str, i: int) -> bool:
    j = i
    while j < len(source) and source[j] in "$@":
        j += 1
    return j > i and j < len(source) and source[j] == "\"" and (i == 0 or not (source[i - 1].isalnum() or source[i - 1] == "_"))


IDENTIFIER_BEFORE = re.compile(r"([A-Za-z_][\w.]*)\s*$")


def _identifier_before(text: str, end: int) -> str | None:
    window = text[max(0, end - 256):end]
    match = IDENTIFIER_BEFORE.search(window)
    return match.group(1) if match else None
NAMED_ARGUMENT = re.compile(r"^\s*([A-Za-z_]\w*)\s*:(?!:)")


@dataclass(frozen=True)
class CsContext:
    kind: str  # "member" (assignment, named argument, expression body) or "call"
    name: str
    argument: int = 0
    attribute: bool = False


def csharp_context(blanked: str, start: int) -> CsContext | None:
    depth = 0
    commas = 0
    i = start - 1
    segment_start = start
    while i >= 0:
        c = blanked[i]
        if c in ")]}":
            depth += 1
        elif c in "([{":
            if depth > 0:
                depth -= 1
            else:
                if commas == 0:
                    named = NAMED_ARGUMENT.match(blanked[i + 1:segment_start])
                    if named:
                        return CsContext("member", named.group(1))
                if c != "(":
                    return None
                before = _identifier_before(blanked, i)
                if not before:
                    return None
                head = blanked[max(0, i - 512):i].rstrip()[: -len(before)].rstrip()
                # An attribute's bracket opens a line; an indexer's or a
                # collection expression's follows an expression.
                attribute = head.endswith("[") and (head[:-1].rstrip()[-1:] in {"", ";", "{", "}", "]"})
                return CsContext("call", before, commas, attribute=attribute)
        elif depth == 0:
            if c == ";":
                return None
            if c == ",":
                if commas == 0:
                    named = NAMED_ARGUMENT.match(blanked[i + 1:segment_start])
                    if named:
                        return CsContext("member", named.group(1))
                commas += 1
                segment_start = i
            elif c == "=" and commas == 0:
                nxt = blanked[i + 1] if i + 1 < len(blanked) else ""
                prev = blanked[i - 1] if i > 0 else ""
                if nxt == "=" or prev in "=!<>":
                    pass
                else:
                    target_end = i - 1 if prev in "+?" else i
                    if prev == "?" and i > 1 and blanked[i - 2] == "?":
                        target_end = i - 2
                    before = _identifier_before(blanked, i if nxt == ">" else target_end)
                    return CsContext("member", before) if before else None
        i -= 1
    return None


USER_FACING_MEMBER = re.compile(
    r"(?:Title|Header|Heading|Content|Text|Watermark|Placeholder|Description|Label|Subtitle|"
    r"Message|Status|Detail|Details|Notice|Hint|Tip|ToolTip|Caption|Summary|Body|Prompt|"
    r"Footnote|AutomationName|HelpText|Warning|Error|Reason|Issue)$"
)
TITLE_MEMBER = re.compile(r"^(?:Title|Header|ConfirmLabel|CancelLabel|CloseLabel|ActionLabel|\w*Button(?:Label|Text|Content))$")
USER_FACING_CALL = re.compile(
    r"(?:^|\.)(?:Fail|Warn|Info|Notify\w*|Show\w*|SetStatus|SetTip|SetName|SetHelpText|Report\w*)$"
)
LOGGING_CALL = re.compile(
    r"(?:^|\.)(?:Log\w*|WriteLine|Write|Trace\w*|Assert|nameof|Debug\.\w+|Console\.\w+)$|(?:^|\.)(?:Debug|Trace|Console)\."
)
TITLE_CALLS = {"Define": 1, "NativeMenuItem": 0, "MenuItem": 0}
CODE_VALUE = re.compile(r"^[a-z0-9_.:/\\#@-]*$")
SQL_START = re.compile(r"^(?:SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|WITH|PRAGMA|SHOW|EXPLAIN|BEGIN|COMMIT|SET|VALUES)\b")
PROSE = re.compile(r"^[\"“(]?[A-Z][\w'’-]*[,:]?\s+[^\s]")


def _normalized(name: str) -> str:
    name = name.rsplit(".", 1)[-1].lstrip("_")
    return name[:1].upper() + name[1:]


def csharp_strings(path: str, source: str) -> list[UiString]:
    literals, blanked = scan_csharp(source)
    line_starts = [0] + [m.end() for m in re.finditer("\n", source)]

    def line_of(offset: int) -> int:
        low, high = 0, len(line_starts)
        while low + 1 < high:
            middle = (low + high) // 2
            if line_starts[middle] <= offset:
                low = middle
            else:
                high = middle
        return low + 1

    decisions: dict[int, tuple[bool, bool, str]] = {}  # index -> (checked, title, context)
    strings: list[UiString] = []
    for index, literal in enumerate(literals):
        value = literal.value
        if literal.parent is not None:
            checked, _, context = decisions[literal.parent]
            decisions[index] = (checked, False, context)
            if checked and value.strip() and not CODE_VALUE.match(value):
                strings.append(UiString(path, line_of(literal.start), value, context))
            continue
        context = csharp_context(blanked, literal.start)
        checked = False
        title = False
        label = "C# string"
        if context and context.kind == "call":
            callee = context.name
            if context.attribute or LOGGING_CALL.search(callee):
                decisions[index] = (False, False, label)
                continue
            last = callee.rsplit(".", 1)[-1]
            label = f"C# {last}() argument"
            if USER_FACING_CALL.search(callee):
                checked = True
            if TITLE_CALLS.get(last) == context.argument and (last != "Define" or path.endswith("/BuiltInCommands.cs")):
                checked = title = True
        elif context:
            name = _normalized(context.name)
            label = f"C# {name}" if name else label
            if USER_FACING_MEMBER.search(name):
                checked = True
            if TITLE_MEMBER.match(name):
                checked = title = True
        visible = value.replace(HOLE, " ").strip()
        if not visible or CODE_VALUE.match(visible):
            checked = title = False
        elif not checked and PROSE.match(visible) and not SQL_START.match(visible):
            checked = True
        decisions[index] = (checked, title, label)
        if checked:
            strings.append(UiString(path, line_of(literal.start), value, label, title))
    return strings


# --- Driver -----------------------------------------------------------------

@dataclass(frozen=True)
class AllowEntry:
    rule: str
    path: str
    text: str
    reason: str

    def allows(self, finding: Finding) -> bool:
        return (
            self.rule in {"*", finding.rule}
            and fnmatch.fnmatchcase(finding.path, self.path)
            and self.text in {finding.fragment, finding.text}
        )


def read_allowlist(path: Path) -> list[AllowEntry]:
    if not path.exists():
        return []
    entries = []
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        parts = [part.strip() for part in line.split(" | ")]
        if len(parts) != 4 or not all(parts):
            raise SystemExit(f"{path}:{number}: expected 'rule | path glob | text | reason' with a reason")
        entries.append(AllowEntry(*parts))
    return entries


def source_files(root: Path, prefixes: list[str], excludes: list[str]) -> list[str]:
    files = []
    for directory, subdirectories, names in os.walk(root / "src"):
        subdirectories[:] = sorted(d for d in subdirectories if d not in SKIPPED_DIRECTORIES)
        for name in sorted(names):
            if not name.endswith((".axaml", ".cs")) or name.endswith((".g.cs", ".Designer.cs")):
                continue
            relative = (Path(directory) / name).relative_to(root).as_posix()
            if prefixes and not any(_under(relative, prefix) for prefix in prefixes):
                continue
            if any(_under(relative, prefix) for prefix in excludes):
                continue
            files.append(relative)
    return files


def _under(relative: str, prefix: str) -> bool:
    prefix = prefix.strip("/")
    if any(c in prefix for c in "*?["):
        return fnmatch.fnmatchcase(relative, prefix) or fnmatch.fnmatchcase(relative, prefix + "/*")
    return relative == prefix or relative.startswith(prefix + "/")


def lint(root: Path, files: list[str], allowlist: list[AllowEntry]) -> list[Finding]:
    findings = []
    for relative in files:
        data = (root / relative).read_bytes()
        if relative.endswith(".axaml"):
            strings = axaml_strings(relative, data)
        else:
            strings = csharp_strings(relative, data.decode("utf-8-sig"))
        for item in strings:
            findings.extend(f for f in check(item) if not any(e.allows(f) for e in allowlist))
    return findings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--paths", nargs="+", default=["src"], metavar="PREFIX",
                        help="only lint files under these repository-relative prefixes or globs")
    parser.add_argument("--exclude", nargs="+", default=[], metavar="PREFIX",
                        help="skip files under these prefixes or globs")
    parser.add_argument("--allowlist", type=Path, default=DEFAULT_ALLOWLIST)
    parser.add_argument("--root", type=Path, default=REPOSITORY, help=argparse.SUPPRESS)
    parser.add_argument("--summary", action="store_true", help="print only per-rule counts")
    arguments = parser.parse_args(argv)

    files = source_files(arguments.root, arguments.paths, arguments.exclude)
    findings = lint(arguments.root, files, read_allowlist(arguments.allowlist))
    if not arguments.summary:
        for finding in findings:
            print(finding.render())
    counts: dict[str, int] = {}
    for finding in findings:
        counts[finding.rule] = counts.get(finding.rule, 0) + 1
    breakdown = ", ".join(f"{rule} {count}" for rule, count in sorted(counts.items()))
    if findings:
        print(f"UI copy: {len(findings)} findings in {len(files)} files ({breakdown}).", file=sys.stderr)
        return 1
    print(f"UI copy passed: {len(files)} files checked.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
