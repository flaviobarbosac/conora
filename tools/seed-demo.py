"""Seed a local demo account with realistic data for store screenshots.

Local development only: talks to http://localhost:5080 (local API + local Postgres).
Usage: python tools/seed-demo.py
"""
import json
import urllib.error
import urllib.request

API = "http://localhost:5080"

# Local-only demo credentials (never used outside localhost).
DEMO_NAME = "Mariana Souza"
DEMO_EMAIL = "demo.loja@conora.local"
DEMO_CPF = "288.684.721-63"
DEMO_PASSWORD = "Demo-Conora-2026!"


def call(method, path, body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as resp:
            raw = resp.read()
            return resp.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as err:
        return err.code, err.read().decode(errors="replace")


def main():
    status, _ = call("POST", "/auth/register", {
        "name": DEMO_NAME, "email": DEMO_EMAIL, "cpf": DEMO_CPF, "password": DEMO_PASSWORD})
    print("register", status)
    status, auth = call("POST", "/auth/login", {"email": DEMO_EMAIL, "password": DEMO_PASSWORD})
    print("login", status)
    token = auth["accessToken"]

    status, cats = call("GET", "/categories", token=token)
    cat = {c["code"]: c["id"] for c in cats if c.get("code")}

    accounts = {}
    for name, kind in (("Conta corrente", 0), ("Carteira", 1), ("Poupança", 3)):
        status, acc = call("POST", "/accounts", {"name": name, "kind": kind}, token)
        print("account", name, status)
        if isinstance(acc, dict):
            accounts[name] = acc["id"]
    main_acc = accounts.get("Conta corrente")

    # (type, amount, day, description, category code)
    month_entries = [
        (1, 7200.00, 5, "Salário", "INC_SALARY"),
        (0, 1850.00, 5, "Aluguel", "ESS_RENT"),
        (0, 980.40, 7, "Supermercado do mês", "ESS_MARKET"),
        (0, 215.30, 10, "Conta de luz", "ESS_POWER"),
        (0, 119.90, 10, "Internet", "ESS_INTERNET"),
        (0, 650.00, 10, "Mensalidade escolar", "ESS_SCHOOL"),
        (0, 320.00, 12, "Combustível", "ESS_FUEL"),
        (0, 186.50, 13, "Restaurante em família", "SOC_RESTAURANT"),
        (0, 55.90, 14, "Streaming", "SOC_STREAM"),
        (0, 92.70, 15, "Farmácia", "ESS_MEDS"),
        (0, 248.60, 15, "Feira e hortifruti", "ESS_FAIR"),
    ]
    for ym in ("2026-09", "2026-10"):
        for etype, amount, day, desc, code in month_entries:
            body = {"type": etype, "amount": amount, "occurredAt": f"{ym}-{day:02d}T12:00:00Z",
                    "description": desc, "competenceYm": ym, "accountId": main_acc,
                    "categoryId": cat.get(code)}
            status, res = call("POST", "/entries", body, token)
            if status >= 300:
                print("entry", ym, desc, status, str(res)[:200])

    plan = {"ESS_RENT": 1850, "ESS_MARKET": 1100, "ESS_POWER": 230, "ESS_INTERNET": 120,
            "ESS_SCHOOL": 650, "ESS_FUEL": 400, "SOC_RESTAURANT": 250, "SOC_STREAM": 60,
            "ESS_MEDS": 150, "ESS_FAIR": 300}
    lines = [{"categoryId": cat[c], "plannedAmount": v} for c, v in plan.items() if c in cat]
    for ym in ("2026-09", "2026-10"):
        status, res = call("PUT", f"/budgets/{ym}", {"mode": 0, "lines": lines}, token)
        print("budget", ym, status, "" if status < 300 else str(res)[:300])

    seed_diagnosis(token)


def seed_diagnosis(token):
    """Register the income source (diagnosis) and mark onboarding as complete."""
    for ym in ("2026-09", "2026-10"):
        status, res = call("POST", "/diagnosis/income-sources", {
            "name": "Salário", "competenceYm": ym, "gross": 8400, "inss": 828.39, "ir": 371.61, "tithe": 0}, token)
        print("income source", ym, status, "" if status < 300 else str(res)[:200])
    status, res = call("PUT", "/family/profile", {"name": DEMO_NAME}, token)
    print("profile name", status)
    status, res = call("POST", "/family/onboarding/complete", None, token)
    print("onboarding complete", status, "" if status < 300 else str(res)[:200])


if __name__ == "__main__":
    main()
