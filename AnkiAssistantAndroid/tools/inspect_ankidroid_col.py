import sqlite3
import sys

sys.stdout.reconfigure(encoding="utf-8")
path = sys.argv[1]
db = sqlite3.connect(path)
cur = db.cursor()
tables = [r[0] for r in cur.execute(
    "select name from sqlite_master where type='table'").fetchall()]
print("表:", tables)

for table, cols in (("decks", "id, name"), ("notetypes", "id, name")):
    if table in tables:
        print(f"--- {table}")
        for row in cur.execute(f"select {cols} from {table}"):
            print("   ", row)

print("笔记数:", cur.execute("select count(*) from notes").fetchone()[0])
for nid, mid, flds in cur.execute("select id, mid, flds from notes limit 6"):
    print("   note", nid, "mid", mid, "->", flds.replace(chr(31), " | ")[:80])
