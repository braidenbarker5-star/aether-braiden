"""Aether backend tests: email/JWT authentication, lockout and Stripe pricing/checkout/status."""
import os
import uuid
import pytest
import requests

BASE_URL: str = os.environ.get("REACT_APP_BACKEND_URL", "http://127.0.0.1:8001").rstrip("/")
API: str = f"{BASE_URL}/api"

ADMIN_EMAIL: str = os.environ.get("AETHER_TEST_ADMIN_EMAIL", "admin@aether.dev")
ADMIN_PASSWORD: str = os.environ.get("AETHER_TEST_ADMIN_PASSWORD", "aether_admin_2026")


def _new_email() -> str:
    return f"test_{uuid.uuid4().hex[:10]}@aether.dev"


# ──────────────── Stripe pricing ────────────────
class TestStripePricing:
    def test_pricing_returns_six_tiers(self) -> None:
        r = requests.get(f"{API}/stripe/pricing")
        assert r.status_code == 200
        data = r.json()
        tiers = {t["tier_id"]: t for t in data["tiers"]}
        assert set(tiers.keys()) == {
            "solo_annual", "founding_lifetime",
            "credits_starter", "credits_builder", "credits_workshop", "credits_wholesale",
        }
        assert tiers["solo_annual"]["amount"] == 89.0
        assert tiers["founding_lifetime"]["amount"] == 299.0
        assert tiers["credits_starter"]["amount"] == 19.0
        assert tiers["credits_builder"]["amount"] == 49.0
        assert tiers["credits_workshop"]["amount"] == 149.0
        assert tiers["credits_wholesale"]["amount"] == 499.0


# ──────────────── Stripe checkout ────────────────
@pytest.fixture(scope="module")
def auth_session() -> requests.Session:
    s = requests.Session()
    r = s.post(f"{API}/auth/login", json={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD})
    if r.status_code != 200:
        pytest.skip("Admin login failed; cannot test authenticated endpoints")
    return s


class TestStripeCheckout:
    def test_checkout_requires_auth(self) -> None:
        r = requests.post(f"{API}/stripe/checkout", json={"tier_id": "credits_starter", "origin_url": "https://example.com"})
        assert r.status_code == 401

    def test_checkout_invalid_tier(self, auth_session: requests.Session) -> None:
        r = auth_session.post(f"{API}/stripe/checkout", json={"tier_id": "bogus_tier", "origin_url": "https://example.com"})
        assert r.status_code == 400

    def test_checkout_valid_creates_session(self, auth_session: requests.Session) -> None:
        r = auth_session.post(f"{API}/stripe/checkout", json={"tier_id": "credits_starter", "origin_url": "https://example.com"})
        assert r.status_code == 200, r.text
        data = r.json()
        assert "url" in data and data["url"].startswith("http")
        assert "session_id" in data
        # Store for status test
        TestStripeCheckout._last_session_id = data["session_id"]

    def test_status_own_session(self, auth_session: requests.Session) -> None:
        sid = getattr(TestStripeCheckout, "_last_session_id", None)
        if not sid:
            pytest.skip("No session id from checkout test")
        r = auth_session.get(f"{API}/stripe/status/{sid}")
        assert r.status_code == 200, r.text
        data = r.json()
        assert "payment_status" in data
        assert "status" in data

    def test_status_other_user_returns_403(self, auth_session: requests.Session) -> None:
        # create a session as admin
        r = auth_session.post(f"{API}/stripe/checkout", json={"tier_id": "credits_starter", "origin_url": "https://example.com"})
        sid = r.json()["session_id"]
        # signup a new user and try to access admin's session
        other = requests.Session()
        other.post(f"{API}/auth/signup", json={"email": _new_email(), "password": "hunter22"})
        r2 = other.get(f"{API}/stripe/status/{sid}")
        assert r2.status_code == 403

    def test_status_requires_auth(self) -> None:
        r = requests.get(f"{API}/stripe/status/cs_test_fake")
        assert r.status_code == 401
