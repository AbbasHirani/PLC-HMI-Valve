#!/usr/bin/env python3
"""
Are both logs still recording?

Written for one question: segment backup has only ever been switched on together with
StorageDevice = SDX51, and SDX51 alone is known to stop the audit trail in simulation, so
backup has been carrying blame it may not deserve. This counts what is actually on disk
before and after a runtime session so the two can be told apart.

  python check_logs.py --save     take a baseline
  python check_logs.py            compare against it

Reads a copy of each database, WAL included - the runtime holds the live files open and
recent writes sit in the -wal until it checkpoints, so a plain copy under-reports.
"""
import argparse, glob, json, os, shutil, sqlite3, tempfile

AUDIT_ROOT = r"C:\UnifiedArchive\AUTDB"
ALARM_ROOT = r"C:\UnifiedArchive\ALGDB"
STATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".log_baseline.json")


def open_copy(db):
    """Copy db + WAL + SHM to temp and open it. Copying the .db3 alone loses recent writes."""
    d = tempfile.mkdtemp()
    for ext in ("", "-wal", "-shm"):
        if os.path.exists(db + ext):
            shutil.copy2(db + ext, os.path.join(d, os.path.basename(db) + ext))
    return sqlite3.connect(os.path.join(d, os.path.basename(db)))


def segments(root):
    hits = glob.glob(os.path.join(root, "**", "*.db3"), recursive=True)
    return sorted(h for h in hits if not h.endswith(("-journal", "-wal", "-shm")))


def count(db, table):
    try:
        con = open_copy(db)
        try:
            names = [r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")]
            if table not in names:
                return 0
            return con.execute("SELECT COUNT(*) FROM [%s]" % table).fetchone()[0]
        finally:
            con.close()
    except Exception:
        return 0


def snapshot():
    audit = sum(count(db, "AuditTrail") for db in segments(AUDIT_ROOT))
    # Alarm rows live in the dated segment files; the ...AlarmLoggingDatabase.db3 beside them is
    # only the index of those segments, so it is counted as segments, not as rows.
    alarm_segs = [db for db in segments(ALARM_ROOT) if "AlarmLoggingDatabase" not in db]
    alarm = 0
    for db in alarm_segs:
        con = open_copy(db)
        try:
            for (t,) in con.execute("SELECT name FROM sqlite_master WHERE type='table'"):
                if t.lower().startswith(("alarm", "spl_")) or "log" in t.lower():
                    try:
                        alarm += con.execute("SELECT COUNT(*) FROM [%s]" % t).fetchone()[0]
                    except Exception:
                        pass
        finally:
            con.close()
    return {"audit_rows": audit, "alarm_rows": alarm,
            "audit_segments": len(segments(AUDIT_ROOT)), "alarm_segments": len(alarm_segs)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--save", action="store_true", help="record a baseline instead of comparing")
    a = ap.parse_args()
    now = snapshot()

    if a.save:
        with open(STATE, "w") as f:
            json.dump(now, f, indent=2)
        print("baseline saved")
        for k, v in now.items():
            print("  %-16s %d" % (k, v))
        return

    if not os.path.exists(STATE):
        print("no baseline - run:  python check_logs.py --save")
        return
    with open(STATE) as f:
        was = json.load(f)

    print("%-16s %8s %8s %8s" % ("", "BEFORE", "NOW", "DELTA"))
    for k in ("audit_rows", "alarm_rows", "audit_segments", "alarm_segments"):
        d = now[k] - was.get(k, 0)
        print("%-16s %8d %8d %+8d" % (k, was.get(k, 0), now[k], d))

    print()
    print("AUDIT : %s" % ("RECORDING" if now["audit_rows"] > was.get("audit_rows", 0)
                          else "no new rows"))
    print("ALARM : %s" % ("RECORDING" if now["alarm_rows"] > was.get("alarm_rows", 0)
                          else "no new rows"))
    print()
    print("'no new rows' only means something if you actually did an action that should")
    print("have been logged - an operator command for the trail, a tripped alarm for the")
    print("alarm log. Nothing done, nothing recorded, and that is not a fault.")


if __name__ == "__main__":
    main()
