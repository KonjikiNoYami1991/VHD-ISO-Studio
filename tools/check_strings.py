#!/usr/bin/env python3
"""Verifica che ogni chiave di localizzazione usata nel codice esista in Strings.cs (IT ed EN) e viceversa."""
import re, glob, sys, pathlib
root = pathlib.Path(__file__).resolve().parent.parent / "src" / "VhdIsoStudio"
used = set()
for f in root.rglob("*.xaml"):
    used |= set(re.findall(r'\{l:Tr (\w+)', f.read_text(encoding="utf-8")))
for f in root.rglob("*.cs"):
    s = f.read_text(encoding="utf-8")
    used |= set(re.findall(r'Loc\.(?:T|F|Plain)\("(\w+)"', s))
    # chiavi scelte con espressioni (es. Loc.T(cond ? "A" : "B")): letterali sulle righe che usano Loc.
    for line in s.splitlines():
        if "Loc." in line:
            used |= set(re.findall(r'"([A-Z]\w+)"', line))
    used |= set(re.findall(r'UserFacingException\("(\w+)"', s))
    used |= set(re.findall(r'e\.Add\("(\w+)"\)', s))
    used |= set(re.findall(r'RunBusyAsync\([^;]*?"(\w+)"', s, flags=re.S))
used |= {"AutoSummary_Boot", "AutoSummary_OsMedia", "AutoSummary_OsWim", "AutoSummary_Data"}
table = (root / "Core" / "Strings.cs").read_text(encoding="utf-8")
defined = re.findall(r'\["(\w+)"\] = \(', table)
dups = {k for k in defined if defined.count(k) > 1}
used &= set(defined) | {k for k in used if re.match(r'^(Val|Err|Stage|Status|Tip|Btn|Lbl|Chk|Opt)', k)}
missing = sorted(used - set(defined))
unused = sorted(set(defined) - used - {"AutoSummary_"})
print(f"usate {len(used)}, definite {len(defined)}")
if dups: print("DUPLICATE:", dups)
if missing: print("MANCANTI:", missing)
if unused: print("NON USATE:", unused)
sys.exit(1 if (missing or dups) else 0)
