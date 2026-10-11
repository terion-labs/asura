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

* C# properties that AXAML binds to a button's Content or a menu item's
  Header, when their names end in Label, ActionText, or ButtonText
  ("TestLabel", "HostKeyActionText"): every literal in the property's body is
  a button or menu label and takes title case.
* C# lists that feed a pop-up button or combo box: the items of a "Choices:"
  argument, an ItemsSource assignment, or a member named *Choices or *Options
  (an item's first constructor argument when the items are records), and the
  results of a method the list maps its items through (".Select(Label)").
  Pop-up items are menu items, so they take title case.
* C# positional constructor arguments that start with a capital letter, such
  as an icon catalog's names or a state overlay's labels: one capitalized word
  passed to a record reads as copy, so it is checked like prose.

Rules: spelling (en-US), ellipsis ("..." instead of "…"), secret-store
(hand-written names for the OS secret store; use Asura.Core.PlatformTerms),
preferences ("Preferences" instead of "Settings"), plural ("item(s)"),
title-case for button labels, menu items, tab items, segment labels, window
titles, and their C# equivalents, and vocabulary (the glossary in
docs/ui-writing-guide.md: "folder", not "directory"; no "definition",
"keybindings", "hotkey", or "Toggle"; and the panel nouns Files, Processes,
Statistics, and Database rather than the retired "File Viewer", "Process
Monitor", "Stats", and "Database Viewer"). The vocabulary rule reads AXAML
and the desktop app's C# (src/Asura.App), and skips exception messages,
which name invariants for developers rather than speak to people.

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
    vocabulary: bool = True


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

# The glossary, as (pattern, message). "working directory" and "home
# directory" are the accepted terms of art; a dotted or hyphenated name such
# as git's "safe.directory" is a code value, not a word.
VOCABULARY = [
    (
        re.compile(r"(?<!working )(?<!home )(?<![.\w-])director(?:y|ies)\b", re.IGNORECASE),
        "say “folder”; macOS has folders (“working directory” and “home directory” are fine)",
    ),
    (
        re.compile(r"\bdefinitions?\b", re.IGNORECASE),
        "name what it is (screen, layout, workspace) instead of “definition”",
    ),
    (re.compile(r"\bkey ?bindings?\b", re.IGNORECASE), "say “keyboard shortcuts”"),
    (re.compile(r"\bhot ?keys?\b", re.IGNORECASE), "say “keyboard shortcut”"),
    (re.compile(r"\bToggle\b"), "use a specific verb, or Show/Hide, instead of “Toggle”"),
    (re.compile(r"\bfile viewer\b", re.IGNORECASE), "the panel is called “Files”"),
    (re.compile(r"\bprocess monitor\b", re.IGNORECASE), "the panel is called “Processes”"),
    (re.compile(r"\bStats\b"), "the panel is called “Statistics”"),
    (re.compile(r"\bdatabase viewer\b", re.IGNORECASE), "the panel is called “Database”"),
]
VOCABULARY_CODE_PATHS = ("src/Asura.App/",)

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
    if item.vocabulary and not SQL_START.match(visible.strip()):
        for pattern, message in VOCABULARY:
            for match in pattern.finditer(visible):
                add("vocabulary", match.group(0), message)
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


BOUND_LABEL = re.compile(r"\w*(?:Label|ActionText|ButtonText)$")
BINDING_PATH = re.compile(r"^\{(?:Binding|CompiledBinding|ReflectionBinding)\s*(?:Path=)?([^,}]*)")
XAML_MENU_ITEMS = {"MenuItem", "NativeMenuItem"}


def axaml_title_bindings(source: bytes) -> set[str]:
    """Property names bound to a button's Content or a menu item's Header."""
    names: set[str] = set()

    def start(name: str, attributes: dict[str, str]) -> None:
        local = _local(name)
        if local in XAML_BUTTONS:
            raw = attributes.get("Content")
        elif local in XAML_MENU_ITEMS:
            raw = attributes.get("Header")
        else:
            return
        match = BINDING_PATH.match((raw or "").strip())
        if not match:
            return
        member = re.split(r"[.)]", match.group(1).strip().rstrip(")"))
        member = [part for part in member if part]
        if member and BOUND_LABEL.fullmatch(member[-1].strip()):
            names.add(member[-1].strip())

    parser = xml.parsers.expat.ParserCreate()
    parser.StartElementHandler = start
    try:
        parser.Parse(source, True)
    except xml.parsers.expat.ExpatError:
        return names
    return names


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
    constructor: bool = False


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
                constructor = before == "new" or re.search(r"\bnew$", head) is not None
                return CsContext("call", before, commas, attribute=attribute, constructor=constructor)
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


# A pop-up button's or combo box's items are menu items, so a list that feeds
# one takes title case: a "Choices:" argument, an ItemsSource assignment, or a
# property or field whose name says it holds the choices.
POP_UP_OWNER = re.compile(r"^(?:ItemsSource|Choices|\w+Choices|\w+Options)$")
COLLECTION_PREFIX = re.compile(r"\bnew\s*(?:[A-Za-z_][\w.]*\s*(?:<[^<>;]*>)?\s*)?(?:\[\s*\]|\(\s*\))?\s*$")
AUTO_PROPERTY = re.compile(r"\{\s*get;\s*(?:(?:init|set|private set);\s*)?\}\s*$")
SELECTED_METHOD = re.compile(r"\.Select\(\s*([A-Za-z_]\w*)\s*\)")


def _opener(blanked: str, start: int) -> tuple[int, int] | None:
    """The innermost unclosed bracket before start, and the commas between."""
    depth = 0
    commas = 0
    i = start - 1
    while i >= 0:
        c = blanked[i]
        if c in ")]}":
            depth += 1
        elif c in "([{":
            if depth == 0:
                return i, commas
            depth -= 1
        elif depth == 0 and c == ",":
            commas += 1
        elif depth == 0 and c == ";":
            return None
        i -= 1
    return None


def _closer(blanked: str, opener: int) -> int:
    depth = 0
    for i in range(opener, len(blanked)):
        c = blanked[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth == 0:
                return i
    return len(blanked)


def collection_owner(blanked: str, opener: int) -> str | None:
    """The member a collection expression or initializer is assigned or passed to."""
    if blanked[opener] not in "[{":
        return None
    head = blanked[:opener].rstrip()
    if blanked[opener] == "{":
        prefix = COLLECTION_PREFIX.search(head)
        if not prefix or not prefix.group(0).strip():
            return None
        head = head[: prefix.start()].rstrip()
    if head.endswith("?") or (head.endswith(":") and not re.search(r"[A-Za-z_]\w*\s*:$", head)):
        # A branch of "cond ? [...] : [...]": the owner is the expression's.
        head = _expression_head(head)
    if head.endswith(":") and not head.endswith("::"):
        named = re.search(r"([A-Za-z_]\w*)\s*:$", head)
        return named.group(1) if named else None
    if head.endswith("=>"):
        head = head[:-2].rstrip()
    elif head.endswith("=") and head[-2:-1] not in {"=", "!", "<", ">"}:
        head = head[:-1].rstrip()
    else:
        return None
    head = AUTO_PROPERTY.sub("", head).rstrip()
    before = _identifier_before(head, len(head))
    return _normalized(before) if before else None


def _expression_head(head: str) -> str:
    """What precedes the expression that ends in head: an argument's name, or an assignment."""
    depth = 0
    i = len(head) - 1
    while i >= 0:
        c = head[i]
        if c in ")]}":
            depth += 1
        elif c in "([{":
            if depth == 0:
                break
            depth -= 1
        elif depth == 0 and c in ",;":
            break
        elif depth == 0 and c == "=" and head[i - 1:i] not in {"=", "!", "<", ">"} and head[i + 1:i + 2] not in {"=", ">"}:
            return head[: i + 1]
        i -= 1
    named = NAMED_ARGUMENT.match(head[i + 1:])
    return head[: i + 1] + named.group(0) if named else ""


def _element_collection(blanked: str, literal: Literal) -> int | None:
    """The collection a literal is an item of, directly or as the first argument of an item's constructor."""
    found = _opener(blanked, literal.start)
    if found is None:
        return None
    opener, commas = found
    if blanked[opener] in "[{":
        return opener if _is_whole_element(blanked, opener, literal) else None
    head = blanked[:opener].rstrip()
    before = _identifier_before(head, len(head))
    if commas != 0 or not before or not (before == "new" or re.search(r"\bnew$", head[: -len(before)].rstrip())):
        return None
    if not _is_whole_element(blanked, opener, literal):
        return None
    outer = _opener(blanked, opener)
    if outer is None or blanked[outer[0]] not in "[{":
        return None
    return outer[0]


def _is_whole_element(blanked: str, opener: int, literal: Literal) -> bool:
    leading = blanked[opener + 1:literal.start]
    leading = leading[leading.rfind(",") + 1:]
    trailing = blanked[literal.end:].lstrip()
    return not leading.strip() and trailing[:1] in {",", ")", "]", "}"}


def pop_up_extents(blanked: str) -> tuple[list[tuple[int, int]], set[str]]:
    """Collections that feed a pop-up, and the methods they map their items through."""
    extents = []
    methods: set[str] = set()
    for match in re.finditer(r"[\[{]", blanked):
        owner = collection_owner(blanked, match.start())
        if owner and POP_UP_OWNER.match(owner):
            end = _closer(blanked, match.start())
            extents.append((match.start(), end))
            methods.update(SELECTED_METHOD.findall(blanked[match.start():end]))
    return extents, methods


STRING_METHOD = re.compile(r"\bstring\??\s+([A-Za-z_]\w*)\s*\([^()]*\)\s*(=>|\{)")


def _method_extents(blanked: str, names: set[str]) -> list[tuple[int, int, str]]:
    """The bodies of string-returning methods named in names, as (start, end, name)."""
    extents = []
    for match in STRING_METHOD.finditer(blanked):
        if match.group(1) not in names:
            continue
        if match.group(2) == "{":
            end = _closer(blanked, match.end() - 1)
        else:
            end = match.end()
            depth = 0
            while end < len(blanked) and not (blanked[end] == ";" and depth == 0):
                depth += 1 if blanked[end] in "([{" else -1 if blanked[end] in ")]}" else 0
                end += 1
        extents.append((match.end(), end, match.group(1)))
    return extents


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


STRING_PROPERTY = re.compile(r"\bstring\??\s+([A-Za-z_]\w*)\s*(=>|\{)")


def _property_extents(blanked: str, names: set[str]) -> list[tuple[int, int, str]]:
    """The bodies of string properties named in names, as (start, end, name)."""
    extents = []
    for match in STRING_PROPERTY.finditer(blanked):
        name = match.group(1)
        if name not in names:
            continue
        i = match.end()
        depth = 0
        while i < len(blanked):
            c = blanked[i]
            if c in "([{":
                depth += 1
            elif c in ")]}":
                if depth == 0:
                    break  # The closing brace of a "{ get => … }" body.
                depth -= 1
            elif c == ";" and depth == 0 and match.group(2) == "=>":
                break
            i += 1
        extents.append((match.end(), i, name))
    return extents


def csharp_strings(path: str, source: str, title_properties: set[str] | frozenset[str] = frozenset()) -> list[UiString]:
    literals, blanked = scan_csharp(source)
    label_bodies = _property_extents(blanked, set(title_properties)) if title_properties else []
    pop_ups, pop_up_methods = pop_up_extents(blanked)
    pop_up_bodies = _method_extents(blanked, pop_up_methods) if pop_up_methods else []
    vocabulary = path.startswith(VOCABULARY_CODE_PATHS)
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
                strings.append(UiString(
                    path, line_of(literal.start), value, context,
                    vocabulary=vocabulary and not context.endswith("Exception() argument")))
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
        body = next((name for start, end, name in label_bodies if start <= literal.start < end), None)
        if body is not None:
            # A button or menu label returned by a property the markup binds.
            checked = title = True
            label = f"C# {body} (bound as a button or menu label)"
        collection = _element_collection(blanked, literal)
        if collection is not None and any(start == collection for start, _ in pop_ups):
            checked = title = True
            label = f"C# {collection_owner(blanked, collection)} item (a pop-up menu item)"
        mapper = next((name for start, end, name in pop_up_bodies if start <= literal.start < end), None)
        if mapper is not None:
            checked = title = True
            label = f"C# {mapper}() result (a pop-up menu item)"
        visible = value.replace(HOLE, " ").strip()
        if not visible or CODE_VALUE.match(visible):
            checked = title = False
        elif not checked and PROSE.match(visible) and not SQL_START.match(visible):
            checked = True
        elif not checked and context and context.constructor and visible[:1].isupper() and not SQL_START.match(visible):
            # A positional record argument, such as an icon catalog's name or
            # a state label: one capitalized word reads as copy, not code.
            checked = True
            label = f"C# {context.name}() argument"
        decisions[index] = (checked, title, label)
        if checked:
            exception = context is not None and context.kind == "call" and (
                context.name.endswith("Exception") or context.name.rsplit(".", 1)[-1].startswith("ThrowIf"))
            strings.append(UiString(
                path, line_of(literal.start), value, label, title,
                vocabulary=vocabulary and not exception))
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
    # Every view's bindings, not only the linted files': a view model's label
    # is a button label because some view, anywhere, binds it as one.
    title_properties: set[str] = set()
    for relative in source_files(root, ["src"], []):
        if relative.endswith(".axaml"):
            title_properties |= axaml_title_bindings((root / relative).read_bytes())
    for relative in files:
        data = (root / relative).read_bytes()
        if relative.endswith(".axaml"):
            strings = axaml_strings(relative, data)
        else:
            strings = csharp_strings(relative, data.decode("utf-8-sig"), title_properties)
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
