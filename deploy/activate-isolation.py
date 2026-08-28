"""Desktop faollashtirish HAR EGANI O'Z do'koniga bog'lashini tekshiradi.

Ishlatish:
    1. API ni ishga tushiring (Development, 5099-port).
    2. python deploy/activate-isolation.py

Nima uchun kerak: ega sozlamalardan desktopni yuklab oladi va uni o'z
login-paroli bilan faollashtiradi. Agar bu bog'lanish noto'g'ri ishlasa,
do'kon BOSHQA do'konning ma'lumotini tortib olardi — va buni hech qanday
xato ko'rsatmasdi, chunki ma'lumot ko'rinishi bo'yicha to'g'ri bo'lardi.

Skript o'zidan keyin tozalaydi: sinov do'konlari o'chiriladi, hisoblar esa
bloklanadi (ularni o'chirib bo'lmaydi — audit jurnali ularga ishora qiladi
va u ataylab o'chirilmaydigan qilingan).
"""
import io, json, os, re, subprocess, sys, urllib.request, urllib.error, uuid

API = "http://127.0.0.1:5099"
PSQL = r"C:\Program Files\PostgreSQL\17\bin\psql.exe"
PASSWORD = "Sinov12345!"

A_MARKET, B_MARKET = 901, 902
A_OWNER = "11111111-aaaa-4aaa-8aaa-111111111111"
A_SELLER = "22222222-aaaa-4aaa-8aaa-222222222222"
B_OWNER = "33333333-bbbb-4bbb-8bbb-333333333333"

passed = failed = 0


def check(label, ok, detail=""):
    global passed, failed
    if ok:
        passed += 1
        print(f"  [OK]   {label}")
    else:
        failed += 1
        print(f"  [XATO] {label}" + (f" - {detail}" if detail else ""))


def pw():
    d = json.load(io.open(r"d:/Projects/Buildix/Buildix.API/appsettings.Development.json",
                          encoding="utf-8-sig"))
    return re.search(r"Password=([^;]*)", d["ConnectionStrings"]["DefaultConnection"]).group(1)


def sql(q, quiet=False):
    env = dict(os.environ, PGPASSWORD=pw())
    r = subprocess.run([PSQL, "-h", "localhost", "-p", "2025", "-U", "postgres",
                        "-d", "BuildixDB", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-c", q],
                       capture_output=True, text=True, env=env)
    if r.returncode != 0 and not quiet:
        raise SystemExit("SQL xato: " + r.stderr[:400])
    return r.stdout.strip()


def call(method, path, body=None, key=None):
    req = urllib.request.Request(API + path, method=method,
                                 data=json.dumps(body).encode() if body is not None else None)
    req.add_header("Content-Type", "application/json")
    if key:
        req.add_header("X-Terminal-Key", key)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw.strip() else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw[:300]


def activate(username, name):
    return call("POST", "/api/pairing/activate", {
        "username": username, "password": PASSWORD,
        "subdomain": None, "terminalName": name,
    })


# ── Sinov do'konlari ──────────────────────────────────────────────────────
# Parol hash'i bcrypt; python tomonida yaratamiz.
try:
    import bcrypt
except ImportError:
    raise SystemExit("`bcrypt` kerak: pip install bcrypt")

h = bcrypt.hashpw(PASSWORD.encode(), bcrypt.gensalt(rounds=11, prefix=b"2a")).decode()
suffix = uuid.uuid4().hex[:6]

sql(f"""
begin;
insert into "Users" ("Id","FullName","Username","PasswordHash","Role","IsActive","IsDeleted",
                     "ShiftStatus","IsPermissionsCustomized","CreatedAt","UpdatedAt")
values ('{A_OWNER}','A Ega','a.ega.{suffix}','{h}',1,true,false,0,false,now(),now()),
       ('{A_SELLER}','A Kassir','a.kassir.{suffix}','{h}',3,true,false,0,false,now(),now()),
       ('{B_OWNER}','B Ega','b.ega.{suffix}','{h}',1,true,false,0,false,now(),now());
insert into "Markets" ("Id","Name","Subdomain","IsActive","CreatedAt","OwnerId","UpdatedAt")
values ({A_MARKET},'A Dokon','a-dokon-{suffix}',true,now(),'{A_OWNER}',now()),
       ({B_MARKET},'B Dokon','b-dokon-{suffix}',true,now(),'{B_OWNER}',now());
update "Users" set "MarketId"={A_MARKET} where "Id" in ('{A_OWNER}','{A_SELLER}');
update "Users" set "MarketId"={B_MARKET} where "Id"='{B_OWNER}';
commit;""")
print(f"ikkita sinov do'koni yaratildi ({A_MARKET}, {B_MARKET})\n")

try:
    print("=== 1. A egasi o'z do'koniga bog'lanadi ===")
    code, body = activate(f"a.ega.{suffix}", "A kassa")
    check("faollashtirish o'tdi", code == 200, f"HTTP {code}: {body}")
    key_a = body.get("key") if code == 200 else None
    check("O'Z do'koni raqami qaytdi", code == 200 and body.get("marketId") == A_MARKET,
          str(body.get("marketId") if code == 200 else body))

    print("\n=== 2. A kaliti FAQAT A do'konining ma'lumotini ko'radi ===")
    code, pull = call("GET", "/api/sync/pull?since=2000-01-01T00:00:00Z", key=key_a)
    check("tortish ishladi", code == 200, f"HTTP {code}")
    if code == 200:
        check("do'kon — A", pull["market"]["id"] == A_MARKET, str(pull["market"]["id"]))
        names = sorted(u["username"] for u in pull["users"])
        check("faqat A xodimlari keldi",
              names == sorted([f"a.ega.{suffix}", f"a.kassir.{suffix}"]), str(names))
        check("B egasi KELMADI", f"b.ega.{suffix}" not in names, str(names))

    print("\n=== 3. Kassir faollashtira olmaydi ===")
    code, body = activate(f"a.kassir.{suffix}", "Kassir urinishi")
    check("rad etildi", code == 400, f"HTTP {code}")
    check("sabab tushunarli", code == 400 and "EGASI" in str(body.get("message", "")),
          str(body)[:120])

    print("\n=== 4. Noto'g'ri parol ===")
    code, body = call("POST", "/api/pairing/activate", {
        "username": f"a.ega.{suffix}", "password": "XATO", "subdomain": None,
        "terminalName": "Urinish",
    })
    check("rad etildi", code == 400, f"HTTP {code}")

    print("\n=== 5. B egasi O'Z do'koniga bog'lanadi ===")
    code, body = activate(f"b.ega.{suffix}", "B kassa")
    check("faollashtirish o'tdi", code == 200, f"HTTP {code}: {body}")
    key_b = body.get("key") if code == 200 else None
    check("O'Z do'koni raqami qaytdi", code == 200 and body.get("marketId") == B_MARKET,
          str(body.get("marketId") if code == 200 else body))
    check("kalitlar har xil", key_a != key_b)

    print("\n=== 6. B kaliti A ning ma'lumotini KO'RMAYDI ===")
    code, pull = call("GET", "/api/sync/pull?since=2000-01-01T00:00:00Z", key=key_b)
    if code == 200:
        check("do'kon — B", pull["market"]["id"] == B_MARKET, str(pull["market"]["id"]))
        names = [u["username"] for u in pull["users"]]
        check("A xodimlari KELMADI", not any(n.startswith("a.") for n in names), str(names))

    print("\n=== 7. A do'koniga ikkinchi kompyuter bog'lanmaydi ===")
    code, body = activate(f"a.ega.{suffix}", "Ikkinchi kassa")
    check("rad etildi", code == 400, f"HTTP {code}")
    check("sabab — allaqachon bog'langan",
          code == 400 and "allaqachon" in str(body.get("message", "")), str(body)[:140])

finally:
    # ── Tozalash ──────────────────────────────────────────────────────────
    # Hisoblarni O'CHIRIB bo'lmaydi: audit jurnali ularga ishora qiladi va u
    # ataylab o'chirilmaydigan qilingan. Shuning uchun ular bloklanadi va
    # paroli yaroqsiz qilinadi.
    dead = bcrypt.hashpw(os.urandom(24).hex().encode(),
                         bcrypt.gensalt(rounds=11, prefix=b"2a")).decode()
    sql(f"""
    begin;
    delete from "RefreshTokens" where "UserId" in ('{A_OWNER}','{A_SELLER}','{B_OWNER}');
    delete from "ShopTerminals" where "MarketId" in ({A_MARKET},{B_MARKET});
    update "Users" set "MarketId"=null, "IsActive"=false, "IsDeleted"=true,
                       "PasswordHash"='{dead}', "UpdatedAt"=now()
     where "Id" in ('{A_OWNER}','{A_SELLER}','{B_OWNER}');
    delete from "SyncPushStates" where "MarketId" in ({A_MARKET},{B_MARKET});
    delete from "SyncStates" where "MarketId" in ({A_MARKET},{B_MARKET});
    delete from "Markets" where "Id" in ({A_MARKET},{B_MARKET});
    commit;""", quiet=True)
    print("\ntozalandi")

print(f"\n  O'TDI: {passed}   XATO: {failed}")
sys.exit(0 if failed == 0 else 1)
