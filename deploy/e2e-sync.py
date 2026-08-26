"""Do'kon ↔ bulut zanjirini uchidan-uchiga sinaydi.

Har bir qism alohida test bilan qoplangan, lekin ular BIR-BIRIGA ULANGANDA
ishlashini faqat shu skript ko'rsatadi: bog'lanish, tortish, haqiqiy savdo,
yuborish, uzilish va tiklanish.

IKKI BOSQICH, chunki do'kon API kalitni ISHGA TUSHISHDA oladi:

    # 1. Bog'lanish — kalit chiqadi
    python deploy/e2e-sync.py pair --cloud http://127.0.0.1:5099

    # 2. Do'kon API ni o'sha kalit bilan ko'tarib, qolganini sinash
    python deploy/e2e-sync.py verify --cloud http://127.0.0.1:5099         --shop http://127.0.0.1:5199 --key <kalit>

Do'kon bazasi tashqaridan yaratiladi va shu skript uni o'zgartirmaydi.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import io
import json
import sys
import time
import urllib.error
import urllib.request
import uuid

CONFIG = r"d:\Projects\Buildix\Buildix.API\appsettings.Development.json"
NS = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/"
ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"

PASSED = 0
FAILED = 0


def check(label: str, ok: bool, detail: str = "") -> bool:
    global PASSED, FAILED
    if ok:
        PASSED += 1
        print(f"  [OK]   {label}")
    else:
        FAILED += 1
        print(f"  [XATO] {label}" + (f" — {detail}" if detail else ""))
    return ok


def section(title: str) -> None:
    print(f"\n=== {title} ===")


# ── HTTP ──────────────────────────────────────────────────────────────────────

def request(method: str, url: str, body=None, token: str = "", terminal_key: str = "",
            expect: int | None = None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    if terminal_key:
        req.add_header("X-Terminal-Key", terminal_key)

    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            raw = response.read()
            status = response.status
    except urllib.error.HTTPError as err:
        raw = err.read()
        status = err.code
    except urllib.error.URLError as err:
        return None, 0, str(err)

    payload = None
    if raw:
        try:
            payload = json.loads(raw)
        except json.JSONDecodeError:
            payload = raw.decode("utf-8", "replace")

    if expect is not None and status != expect:
        return payload, status, f"kutilgan {expect}, kelgan {status}: {payload}"
    return payload, status, ""


# ── Token ─────────────────────────────────────────────────────────────────────

def b64(raw: bytes) -> str:
    return base64.urlsafe_b64encode(raw).rstrip(b"=").decode()


def mint(user_id: str, username: str, role: str, market_id: str = "") -> str:
    """Ishlab chiqish kalitidan token yasaydi — parol talab qilinmaydi."""
    jwt = json.load(io.open(CONFIG, encoding="utf-8-sig"))["Jwt"]
    now = int(time.time())
    payload = {
        NS + "nameidentifier": user_id,
        NS + "name": username,
        ROLE: role,
        "jti": str(uuid.uuid4()),
        "iat": now,
        "exp": now + 3600,
        "iss": jwt["Issuer"],
        "aud": jwt["Audience"],
    }
    if market_id:
        payload["MarketId"] = market_id
    head = b64(json.dumps({"alg": "HS256", "typ": "JWT"}, separators=(",", ":")).encode())
    body = b64(json.dumps(payload, separators=(",", ":")).encode())
    signed = f"{head}.{body}"
    sig = hmac.new(jwt["Key"].encode(), signed.encode(), hashlib.sha256).digest()
    return f"{signed}.{b64(sig)}"


def console_segment() -> str:
    return json.load(io.open(CONFIG, encoding="utf-8-sig"))["SuperAdmin"]["ConsoleSegment"]


# ── Kutish ────────────────────────────────────────────────────────────────────

def wait_for(label: str, probe, timeout: int = 180, interval: int = 5):
    """Sinxronizatsiya fon xizmatida ishlaydi — natijani kutish kerak."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        value = probe()
        if value:
            return value
        time.sleep(interval)
    check(label, False, f"{timeout} soniyada sodir bo'lmadi")
    return None


# ── 1-bosqich: bog'lanish ─────────────────────────────────────────────────────

def run_pair(cloud: str, market: int, sa: str, owner: str, seg: str) -> str | None:
    section("0. Bulut javob beryaptimi")
    _, status, _ = request("GET", f"{cloud}/health")
    if not check("bulut javob beradi", status == 200):
        return None

    # Skript QAYTA-QAYTA yuritiladi, ya'ni oldingi o'tishdan qolgan kompyuter
    # bo'lishi mumkin. Uni panel orqali bekor qilamiz — xom SQL bilan emas:
    # shu yo'l ham sinovdan o'tadi va skript haqiqiy ish tartibini takrorlaydi.
    body, _, err = request("GET", f"{cloud}/api/_sa/{seg}/markets/{market}/terminals",
                           token=sa, expect=200)
    if not err and isinstance(body, list):
        stale = [t for t in body if t.get("revokedAtUtc") is None]
        for terminal in stale:
            request("POST", f"{cloud}/api/_sa/{seg}/terminals/{terminal['id']}/revoke",
                    body={}, token=sa)
        if stale:
            print(f"  (oldingi o'tishdan qolgan {len(stale)} kompyuter bekor qilindi)")

    section("1. Bog'lanmagan do'kon holati")
    body, _, err = request("GET", f"{cloud}/api/Markets/sync-status", token=owner, expect=200)
    check("holat so'raldi", not err, err)
    if isinstance(body, dict):
        check("bog'lanmagan deb ko'rsatiladi", body.get("isPaired") is False, str(body))

    section("2. Panel kod beradi")
    body, _, err = request("POST", f"{cloud}/api/_sa/{seg}/markets/{market}/pairing-code",
                           body={}, token=sa, expect=200)
    if not check("kod berildi", not err, err):
        return None
    code = body["code"]
    check("kod ko'rinishi to'g'ri", code.startswith("BX-") and len(code) == 12, code)

    section("3. Kod kalitga almashadi")
    body, status, err = request("POST", f"{cloud}/api/pairing/redeem",
                                body={"code": code, "terminalName": "Sinov kassa"}, expect=200)

    # 429 — XATO EMAS. Bog'lanish yo'li anonim va soatiga o'n urinish bilan
    # cheklangan; skript qayta-qayta yuritilganda shu chegaraga uriladi.
    # Buni nosozlik deb ko'rsatish yolg'on bo'lardi: aksincha, himoya
    # ishlayotganining tasdig'i.
    if status == 429:
        check("tezlik cheklovi ishlayapti (429)", True)
        print("")
        print("  DIQQAT: bog'lanish chegarasiga urildi. To'liq sinov uchun")
        print("  bir soat kuting yoki boshqa manzildan yuriting.")
        return None

    if not check("bog'landi", not err, err):
        return None
    key = body["key"]
    check("do'kon raqami to'g'ri", body["marketId"] == market, str(body))
    check("kalit qaytdi", len(key) > 30)

    section("4. Kod ikkinchi marta ishlamaydi")
    _, status, _ = request("POST", f"{cloud}/api/pairing/redeem",
                           body={"code": code, "terminalName": "O'g'ri"})
    check("takroriy kod rad etildi", status == 400, f"kod {status}")

    section("5. Ikkinchi kompyuter bog'lanmaydi")
    body, _, err = request("POST", f"{cloud}/api/_sa/{seg}/markets/{market}/pairing-code",
                           body={}, token=sa, expect=200)
    if not err:
        _, status, _ = request("POST", f"{cloud}/api/pairing/redeem",
                               body={"code": body["code"], "terminalName": "Ikkinchi"})
        check("ikkinchi kompyuter rad etildi", status == 400, f"kod {status}")

    section("6. Kalitsiz va noto'g'ri kalit")
    _, status, _ = request("GET", f"{cloud}/api/sync/pull")
    check("kalitsiz — 401", status == 401, f"kod {status}")
    _, status, _ = request("GET", f"{cloud}/api/sync/pull", terminal_key="yolgon")
    check("noto'g'ri kalit — 401", status == 401, f"kod {status}")

    return key


# ── 2-bosqich: haqiqiy ish ────────────────────────────────────────────────────

def run_verify(cloud: str, shop: str, market: int, owner: str, sa: str, seg: str, key: str) -> None:
    section("7. Ikkala tomon javob beryaptimi")
    _, status, _ = request("GET", f"{cloud}/health")
    check("bulut javob beradi", status == 200)
    _, status, _ = request("GET", f"{shop}/health")
    if not check("do'kon javob beradi", status == 200):
        return

    section("8. Do'kon bulutdan ma'lumot oladi")
    print("  (fon xizmati kutilmoqda)")

    def pulled():
        payload, status_, _ = request("GET", f"{shop}/api/Markets/settings", token=owner)
        return status_ == 200

    if wait_for("do'kon ma'lumot oldi", pulled, timeout=180):
        check("do'kon ma'lumot oldi", True)

    section("9. Do'konda haqiqiy savdo")
    product = {"name": f"Sinov sement {uuid.uuid4().hex[:6]}", "isTemporary": False,
               "salePrice": 12000, "minSalePrice": 10000, "minThreshold": 1,
               "categoryId": None, "unit": 1, "quantity": 100, "costPrice": 9000}
    body, status, err = request("POST", f"{shop}/api/Products/CreateProduct", body=product, token=owner)
    if not check("tovar yaratildi", status in (200, 201), f"kod {status}: {body}"):
        return
    product_id = body["id"]

    body, status, _ = request("POST", f"{shop}/api/Sales", body={"customerId": None}, token=owner)
    if not check("chek ochildi", status in (200, 201), f"kod {status}: {body}"):
        return
    sale_id, sale_number = body["id"], body["saleNumber"]

    item = {"isExternal": False, "productId": product_id, "externalProductName": None,
            "externalCostPrice": None, "quantity": 3, "salePrice": 12000,
            "minSalePrice": 10000, "comment": None}
    body, status, _ = request("POST", f"{shop}/api/Sales/{sale_id}/items", body=item, token=owner)
    check("tovar chekka qo'shildi", status in (200, 201), f"kod {status}: {body}")

    body, status, _ = request("POST", f"{shop}/api/Sales/{sale_id}/checkout",
                              body={"tenders": [{"paymentType": "Cash", "amount": 36000}]},
                              token=owner)
    check("chek yopildi (36 000)", status in (200, 201), f"kod {status}: {body}")

    section("10. Savdo bulutga yetib boradi")
    print("  (fon xizmati kutilmoqda)")

    def in_cloud():
        payload, status_, _ = request("GET", f"{cloud}/api/Sales/{sale_id}", token=owner)
        return payload if status_ == 200 and isinstance(payload, dict) else None

    arrived = wait_for("savdo bulutda paydo bo'ldi", in_cloud, timeout=240)
    if arrived:
        check("savdo bulutda paydo bo'ldi", True)
        check("chek raqami saqlandi", arrived.get("saleNumber") == sale_number,
              f"{arrived.get('saleNumber')} != {sale_number}")
        check("summa to'g'ri", float(arrived.get("totalAmount") or 0) == 36000.0,
              str(arrived.get("totalAmount")))
        check("qatorlar yetib keldi", len(arrived.get("items") or []) == 1,
              str(len(arrived.get("items") or [])))
        check("holat to'g'ri", arrived.get("status") in ("Paid", "Completed"),
              str(arrived.get("status")))

    section("11. Tovar ham yetib boradi")
    payload, status, _ = request("GET", f"{cloud}/api/Products/GetProduct/{product_id}", token=owner)
    check("tovar bulutda", status == 200, f"kod {status}")
    if status == 200 and isinstance(payload, dict):
        check("qoldiq yechilgan (97)", float(payload.get("quantity") or 0) == 97.0,
              str(payload.get("quantity")))

    section("12. Holat yangi deb ko'rsatiladi")
    body, _, err = request("GET", f"{cloud}/api/Markets/sync-status", token=owner, expect=200)
    if isinstance(body, dict):
        check("bog'langan", body.get("isPaired") is True, str(body))
        check("ma'lumot yangi", body.get("isFresh") is True, str(body))
        check("kassa nomi ko'rinadi", body.get("terminalName") == "Sinov kassa", str(body))

    section("13. Takroriy yuborish nusxa yaratmaydi")
    print("  (yana bir sikl kutilmoqda)")
    time.sleep(75)
    payload, status, _ = request("GET", f"{cloud}/api/Sales/{sale_id}", token=owner)
    check("savdo o'zgarmagan",
          status == 200 and float(payload.get("totalAmount") or 0) == 36000.0,
          str(payload.get("totalAmount") if isinstance(payload, dict) else payload))
    check("qatorlar takrorlanmagan",
          status == 200 and len(payload.get("items") or []) == 1,
          str(len(payload.get("items") or []) if isinstance(payload, dict) else payload))

    section("14. Kalit bekor qilinsa aloqa uziladi")
    body, _, err = request("GET", f"{cloud}/api/_sa/{seg}/markets/{market}/terminals",
                           token=sa, expect=200)
    if not err and isinstance(body, list):
        terminal_id = next((t["id"] for t in body if t.get("revokedAtUtc") is None), None)
        if terminal_id:
            _, _, err = request("POST", f"{cloud}/api/_sa/{seg}/terminals/{terminal_id}/revoke",
                                body={}, token=sa, expect=200)
            check("kalit bekor qilindi", not err, err)
            _, status, _ = request("GET", f"{cloud}/api/sync/pull", terminal_key=key)
            check("bekor qilingan kalit rad etildi", status == 401, f"kod {status}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("phase", choices=["pair", "verify"])
    parser.add_argument("--cloud", required=True)
    parser.add_argument("--shop", default="")
    parser.add_argument("--key", default="")
    parser.add_argument("--market", type=int, default=9)
    parser.add_argument("--owner", default="41e63206-7031-42aa-b468-ce30a794a3d4")
    args = parser.parse_args()

    cloud, shop, market = args.cloud.rstrip("/"), args.shop.rstrip("/"), args.market
    seg = console_segment()
    sa = mint("00000000-0000-0000-0000-000000000001", "super", "SuperAdmin")
    owner = mint(args.owner, "ega", "Owner", str(market))

    if args.phase == "pair":
        key = run_pair(cloud, market, sa, owner, seg)
        print(f"\n{'=' * 46}")
        print(f"  O'TDI: {PASSED}   XATO: {FAILED}")
        if key:
            print(f"\nKALIT: {key}")
        print(f"{'=' * 46}")
        return 0 if FAILED == 0 and key else 1

    if not args.shop or not args.key:
        print("verify uchun --shop va --key kerak")
        return 2

    run_verify(cloud, shop, market, owner, sa, seg, args.key)
    print(f"\n{'=' * 46}")
    print(f"  O'TDI: {PASSED}   XATO: {FAILED}")
    print(f"{'=' * 46}")
    return 0 if FAILED == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
