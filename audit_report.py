#!/usr/bin/env python3
"""
Turns the WinCC Audit Trail into something a person can read.

The Audit Viewer shows the raw record: HMI_RT_1::V021_Configured, true -> false,
provider "Scripting", alongside an "Event Manager" copy of every row and a stream of
login and audit-start entries. All correct, none of it readable by a client or a
surveyor. This produces "CM79  DISABLED  by USER" instead.

What it does:
  - keeps only Scripting rows, which are the operator's own actions. The Event Manager
    row paired with each one is the system's reaction, not a second action - that pairing
    is what looks like duplication in the viewer.
  - drops login/logout and audit-log-start entries
  - drops writes that did not change anything (false -> false)
  - translates V021 to CM79 via AUDIT_TAG_MAP.csv, because the CM number is project data
    and is not derivable from the tag name
  - collapses the four fault-reset tags written together into one FAULT RESET line

The Audit Trail database stays the record of truth. This is a reading of it.

Usage:
  python audit_report.py                    latest segment, print to console
  python audit_report.py --csv out.csv      also write a CSV
  python audit_report.py --days 7           only the last 7 days
  python audit_report.py --db <path.db3>    a specific segment
"""

import argparse, csv, datetime, glob, os, shutil, sqlite3, sys, tempfile
from collections import OrderedDict

AUDIT_DIR = r"C:\UnifiedArchive\AUTDB"
MAP_CSV   = os.path.join(os.path.dirname(os.path.abspath(__file__)), "AUDIT_TAG_MAP.csv")

# FILETIME: 100 ns ticks since 1601-01-01 UTC. Panel/PC clock here is IST (UTC+5:30);
# the trail stores UTC, so display needs the offset applied.
EPOCH  = datetime.datetime(1601, 1, 1)
TZ_OFF = datetime.timedelta(hours=5, minutes=30)

# tag suffix -> (action when value goes true, action when it goes false)
ACTIONS = {
    "_OpenCmd":    ("OPEN",     None),
    "_CloseCmd":   ("CLOSE",    None),
    "_Configured": ("ENABLED",  "DISABLED"),
}
RESET_SUFFIXES = ("_TimeoutOpenAlarm", "_TimeoutCloseAlarm", "_UnexpMove", "_DirFault")


def to_local(ticks):
    return EPOCH + datetime.timedelta(microseconds=ticks / 10) + TZ_OFF


def load_map(path):
    """V001 -> (CM25, 11-020-A1, DB tank, Filling)"""
    if not os.path.exists(path):
        sys.exit("[ERROR] mapping file not found: %s" % path)
    out = {}
    with open(path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            out[row["HmiTagPrefix"]] = (row["CmNo"], row["ValveTag"], row["Location"], row["Function"])
    return out


def newest_segment():
    hits = glob.glob(os.path.join(AUDIT_DIR, "*", "*.db3"))
    hits = [h for h in hits if "-journal" not in h and "-wal" not in h]
    if not hits:
        sys.exit("[ERROR] no audit segment found under %s" % AUDIT_DIR)
    return max(hits, key=os.path.getmtime)


def read_rows(db_path):
    # copy first - runtime holds the live file open
    tmp = os.path.join(tempfile.gettempdir(), "audit_snapshot.db3")
    shutil.copy2(db_path, tmp)
    con = sqlite3.connect(tmp)
    try:
        return con.execute(
            "SELECT pk_timestamp, ObjectName, User, OldValue, NewValue "
            "FROM AuditTrail WHERE AuditProvider = 'Scripting' ORDER BY pk_timestamp"
        ).fetchall()
    finally:
        con.close()


def truthy(v):
    return str(v).strip().lower() in ("1", "true")


def build(rows, tagmap, since=None):
    events, pending_reset = [], OrderedDict()

    def flush_resets():
        for (sec, user, prefix), _ in list(pending_reset.items()):
            cm = tagmap.get(prefix, (prefix, "", "", ""))
            events.append((sec, cm[0], cm[1], cm[2], "FAULT RESET", user))
            del pending_reset[(sec, user, prefix)]

    for ticks, obj, user, old, new in rows:
        ts = to_local(ticks)
        if since and ts < since:
            continue
        name = (obj or "").replace("HMI_RT_1::", "")
        if not name.startswith("V") or "_" not in name:
            continue                                  # login / audit-start rows
        prefix, suffix = name[:4], name[4:]
        if truthy(old) == truthy(new):
            continue                                  # write that changed nothing

        if suffix in RESET_SUFFIXES:
            # the four clear-alarm tags are written together by one FAULT RESET press
            pending_reset.setdefault((ts.replace(microsecond=0), user, prefix), True)
            continue

        act = ACTIONS.get(suffix)
        if not act:
            continue
        label = act[0] if truthy(new) else act[1]
        if not label:
            continue                                  # command bit being cleared, not an action
        cm = tagmap.get(prefix, (prefix, "", "", ""))
        events.append((ts, cm[0], cm[1], cm[2], label, user))

    flush_resets()
    events.sort(key=lambda e: e[0])
    return events


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db")
    ap.add_argument("--csv")
    ap.add_argument("--days", type=int)
    a = ap.parse_args()

    db = a.db or newest_segment()
    since = None
    if a.days:
        since = datetime.datetime.now() - datetime.timedelta(days=a.days)

    tagmap = load_map(MAP_CSV)
    events = build(read_rows(db), tagmap, since)

    print("MV WESTERLY  -  VALVE OPERATOR ACTION LOG")
    print("source : %s" % os.path.basename(db))
    if since:
        print("period : last %d day(s)" % a.days)
    print("entries: %d\n" % len(events))
    hdr = "%-10s %-8s %-7s %-11s %-14s %-12s %s" % (
        "DATE", "TIME", "CM NO", "VALVE TAG", "LOCATION", "ACTION", "USER")
    print(hdr)
    print("-" * len(hdr))
    for ts, cm, vtag, loc, action, user in events:
        print("%-10s %-8s %-7s %-11s %-14s %-12s %s" % (
            ts.strftime("%d/%m/%Y"), ts.strftime("%H:%M:%S"),
            cm, vtag, loc[:14], action, user or "(none)"))

    if a.csv:
        with open(a.csv, "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["Date", "Time", "CmNo", "ValveTag", "Location", "Action", "User"])
            for ts, cm, vtag, loc, action, user in events:
                w.writerow([ts.strftime("%d/%m/%Y"), ts.strftime("%H:%M:%S"),
                            cm, vtag, loc, action, user])
        print("\nCSV written: %s" % a.csv)


if __name__ == "__main__":
    main()
