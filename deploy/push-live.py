"""Sinxronizatsiyani HAQIQIY bazaga qarab tekshiradi.

Ishlatish:
    1. API ni ishga tushiring (Development, 5099-port).
    2. python deploy/push-live.py

Nima uchun kerak: birlik sinovlari InMemory bazada ishlaydi va u tashqi
kalitlarni UMUMAN tekshirmaydi.

Birlik sinovlari InMemory bazada ishlaydi va u tashqi kalitlarni UMUMAN
tekshirmaydi — ya'ni «FK buzilardi» degan da'vo u yerda isbotlanmaydi.
Bu skript haqiqiy bazaga qarab ishlaydi: otasi yo'q qatorlar 500 emas,
`deferred` bo'lib qaytishi kerak.
"""
import hashlib, io, json, os, re, secrets, subprocess, sys, urllib.request, urllib.error, uuid

API = "http://127.0.0.1:5099"
MARKET = 9
PSQL = r"C:\Program Files\PostgreSQL\17\bin\psql.exe"


def pw():
    d = json.load(io.open(r"d:/Projects/Buildix/Buildix.API/appsettings.Development.json",
                          encoding="utf-8-sig"))
    return re.search(r"Password=([^;]*)", d["ConnectionStrings"]["DefaultConnection"]).group(1)


def sql(q):
    env = dict(os.environ, PGPASSWORD=pw())
    r = subprocess.run([PSQL, "-h", "localhost", "-p", "2025", "-U", "postgres",
                        "-d", "BuildixDB", "-A", "-t", "-c", q],
                       capture_output=True, text=True, env=env)
    if r.returncode != 0:
        raise SystemExit("SQL xato: " + r.stderr[:300])
    return r.stdout.strip()


def push(payload, key):
    req = urllib.request.Request(API + "/api/sync/push", method="POST",
                                 data=json.dumps(payload).encode())
    req.add_header("X-Terminal-Key", key)
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status, json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()[:300]


passed = failed = 0


def check(label, ok, detail=""):
    global passed, failed
    if ok:
        passed += 1
        print(f"  [OK]   {label}")
    else:
        failed += 1
        print(f"  [XATO] {label}" + (f" - {detail}" if detail else ""))


# ── Terminal kaliti ───────────────────────────────────────────────────────
key = secrets.token_urlsafe(32)
# Convert.ToHexString KATTA harfda yozadi va taqqoslash aynan mos
# kelishi kerak.
key_hash = hashlib.sha256(key.encode()).hexdigest().upper()
tid = str(uuid.uuid4())
sql(f"""delete from "ShopTerminals" where "MarketId" = {MARKET};
        insert into "ShopTerminals" ("Id","MarketId","Name","KeyHash","CreatedAt","UpdatedAt")
        values ('{tid}', {MARKET}, 'Jonli sinov', '{key_hash}', now(), now());""")
print("terminal yaratildi\n")

now = "2026-08-28T00:00:00Z"


def blank():
    return {k: [] for k in ["users", "suppliers", "products", "customers", "shifts",
                            "sales", "saleItems", "payments", "debts",
                            "saleReturns", "saleReturnItems",
                            "zakupReceipts", "zakups", "cashMovements", "stockMovements"]}


print("=== 1. Otasi YO'Q qarz — 500 emas, kechiktirilishi kerak ===")
p = blank()
p["debts"] = [{"id": str(uuid.uuid4()), "saleId": str(uuid.uuid4()),
               "customerId": str(uuid.uuid4()), "totalDebt": 500, "remainingDebt": 500,
               "status": 0, "marketId": MARKET, "createdAt": now, "updatedAt": now}]
code, body = push(p, key)
check("500 qaytmadi", code == 200, f"HTTP {code}: {body}")
if code == 200:
    check("qarz kechiktirildi", "Debt" in (body.get("deferred") or {}), json.dumps(body)[:200])

print("\n=== 2. Tovari YO'Q ombor yozuvi ===")
p = blank()
p["stockMovements"] = [{"id": str(uuid.uuid4()), "marketId": MARKET,
                        "productId": str(uuid.uuid4()), "type": 0, "quantity": 5,
                        "resultingQty": 5, "createdAt": now, "updatedAt": now}]
code, body = push(p, key)
check("500 qaytmadi", code == 200, f"HTTP {code}: {body}")
if code == 200:
    check("ombor yozuvi kechiktirildi", "StockMovement" in (body.get("deferred") or {}),
          json.dumps(body)[:200])

print("\n=== 3. To'liq zanjir: xodim + tovar + mijoz + chek + qator + qarz ===")
uid, pid, cid, sid = (str(uuid.uuid4()) for _ in range(4))
p = blank()
p["users"] = [{"id": uid, "marketId": MARKET, "fullName": "Jonli Kassir",
               "username": "jonli-" + uid[:8], "passwordHash": "x", "role": 3,
               "isActive": True, "isDeleted": False, "shiftStatus": 0,
               "isPermissionsCustomized": False, "permissions": [],
               "createdAt": now, "updatedAt": now}]
p["products"] = [{"id": pid, "marketId": MARKET, "name": "Jonli tovar",
                  "costPrice": 1000, "salePrice": 1500, "minSalePrice": 1200,
                  "quantity": 50, "minThreshold": 5, "unit": 0,
                  "isTemporary": False, "isDeleted": False, "isHidden": False,
                  "hidePriceFromSellers": False, "categoryId": 999,
                  "createdAt": now, "updatedAt": now}]
p["customers"] = [{"id": cid, "marketId": MARKET, "phone": "+99890" + uid[:7],
                   "isDeleted": False, "customerType": 0, "isRegular": False,
                   "createdAt": now, "updatedAt": now}]
p["sales"] = [{"id": sid, "marketId": MARKET, "sellerId": uid, "customerId": cid,
               "saleNumber": 9001, "status": 2, "totalAmount": 3000, "paidAmount": 1000,
               "discountAmount": 0, "isDeleted": False, "isOpeningBalance": False,
               "createdAt": now, "updatedAt": now}]
p["saleItems"] = [{"id": str(uuid.uuid4()), "saleId": sid, "productId": pid,
                   "quantity": 2, "costPrice": 1000, "salePrice": 1500,
                   "isExternal": False, "createdAt": now, "updatedAt": now}]
p["debts"] = [{"id": str(uuid.uuid4()), "saleId": sid, "customerId": cid,
               "totalDebt": 3000, "remainingDebt": 2000, "status": 0,
               "marketId": MARKET, "createdAt": now, "updatedAt": now}]
code, body = push(p, key)
check("zanjir qabul qilindi", code == 200, f"HTTP {code}: {body}")
if code == 200:
    check("hamma qator o'tdi", not (body.get("deferred") or {}), json.dumps(body.get("deferred"))[:200])
    print("   qabul qilingan:", json.dumps(body.get("perTable"), ensure_ascii=False))

print("\n=== 4. Bazada haqiqatan bormi ===")
n_debt = sql(f"""select count(*) from "Debts" where "SaleId" = '{sid}';""")
n_user = sql(f"""select count(*) from "Users" where "Id" = '{uid}';""")
cat = sql(f"""select coalesce("CategoryId"::text, 'NULL') from "Products" where "Id" = '{pid}';""")
check("qarz yozildi", n_debt == "1", n_debt)
check("xodim yozildi", n_user == "1", n_user)
check("kategoriya havolasi uzildi", cat == "NULL", cat)

print("\n=== 5. Ma'lumot kelgan vaqt belgilandimi ===")
mark = sql(f"""select coalesce("LastPushAtUtc"::text,'NULL') from "ShopTerminals" where "Id" = '{tid}';""")
check("LastPushAtUtc qo'yildi", mark != "NULL", mark)

# Tozalash
sql(f"""delete from "Debts" where "SaleId" = '{sid}';
        delete from "SaleItems" where "SaleId" = '{sid}';
        delete from "Sales" where "Id" = '{sid}';
        delete from "Customers" where "Id" = '{cid}';
        delete from "Products" where "Id" = '{pid}';
        delete from "Users" where "Id" = '{uid}';
        delete from "ShopTerminals" where "Id" = '{tid}';""")
print("\ntozalandi")

print(f"\n  O'TDI: {passed}   XATO: {failed}")
sys.exit(0 if failed == 0 else 1)
