// Shared session/auth plumbing, loaded before js/api.js on every page.
const Auth = (() => {
  const ACCESS_KEY = 'tae_access';
  const REFRESH_KEY = 'tae_refresh';
  const USER_KEY = 'tae_user';

  function getAccessToken() { return localStorage.getItem(ACCESS_KEY); }
  function getRefreshToken() { return localStorage.getItem(REFRESH_KEY); }
  function getUser() {
    const raw = localStorage.getItem(USER_KEY);
    return raw ? JSON.parse(raw) : null;
  }

  function setSession({ accessToken, refreshToken, user }) {
    localStorage.setItem(ACCESS_KEY, accessToken);
    localStorage.setItem(REFRESH_KEY, refreshToken);
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  }

  function clearSession() {
    localStorage.removeItem(ACCESS_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
  }

  function requireLogin() {
    if (!getAccessToken()) {
      location.href = 'login.html?redirect=' + encodeURIComponent(location.pathname + location.search);
    }
  }

  // Sidebar menu visibility is driven by the role-permissions matrix — see js/nav.js — not by
  // hardcoded role-name checks. `data-admin-only` remains for one-off page controls (e.g.
  // analysis.html's "view all users" toggle) that aren't nav links.
  function applyRoleNav() {
    const user = getUser();
    if (!user) return;
    const isAdmin = user.roleName === 'Administrator';
    document.querySelectorAll('[data-admin-only]').forEach((el) => {
      el.style.display = isAdmin ? '' : 'none';
    });
    const navUser = document.getElementById('navUser');
    if (navUser) navUser.textContent = user.username;
  }

  async function refreshAccessToken() {
    const refreshToken = getRefreshToken();
    if (!refreshToken) return false;
    try {
      const res = await fetch('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });
      if (!res.ok) { clearSession(); return false; }
      const data = await res.json();
      setSession({ accessToken: data.accessToken, refreshToken: data.refreshToken, user: data.user });
      return true;
    } catch {
      return false;
    }
  }

  function logout() {
    const refreshToken = getRefreshToken();
    fetch('/api/auth/logout', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${getAccessToken()}` },
      body: JSON.stringify({ refreshToken }),
    }).finally(() => {
      clearSession();
      location.href = 'login.html';
    });
  }

  return { getAccessToken, getRefreshToken, getUser, setSession, clearSession, requireLogin, applyRoleNav, refreshAccessToken, logout };
})();
