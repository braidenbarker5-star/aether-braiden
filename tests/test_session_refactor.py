"""Smoke tests for the email/JWT authentication endpoints."""
import os

import pytest
import requests

BASE_URL: str = os.environ.get("REACT_APP_BACKEND_URL", "http://127.0.0.1:8001").rstrip("/")
ADMIN_EMAIL: str = os.environ.get("AETHER_TEST_ADMIN_EMAIL", "admin@aether.dev")
ADMIN_PASSWORD: str = os.environ.get("AETHER_TEST_ADMIN_PASSWORD", "aether_admin_2026")


@pytest.fixture
def api_client() -> requests.Session:
    session = requests.Session()
    session.headers.update({"Content-Type": "application/json"})
    return session


class TestAdminAuthSmoke:
    def test_admin_login_returns_200_with_user(self, api_client: requests.Session) -> None:
        response = api_client.post(
            f"{BASE_URL}/api/auth/login",
            json={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
        )
        assert response.status_code == 200, response.text
        user = response.json()
        assert user.get("email") == ADMIN_EMAIL
        assert user.get("role") == "admin"
        assert user.get("credits", 0) >= 0
        assert "access_token" in response.cookies

    def test_me_endpoint_with_cookie_after_login(self, api_client: requests.Session) -> None:
        login = api_client.post(
            f"{BASE_URL}/api/auth/login",
            json={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
        )
        assert login.status_code == 200, login.text
        response = api_client.get(f"{BASE_URL}/api/auth/me")
        assert response.status_code == 200
        assert response.json().get("email") == ADMIN_EMAIL

    def test_me_unauthenticated_returns_401(self, api_client: requests.Session) -> None:
        response = requests.get(f"{BASE_URL}/api/auth/me")
        assert response.status_code == 401
