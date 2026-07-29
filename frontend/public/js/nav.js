// Renders the left sidebar (category picker + permission-filtered menu) and the top-right
// user/logout bar. Loaded after auth.js/api.js, before each page's inline script; pages call
// Nav.render() against empty <div id="sidebarRoot"></div> and <div id="topBarRoot"></div>.
const Nav = (() => {
  const CATEGORY_KEY = 'tae_category';

  const MENU_ITEMS = [
    { label: 'History', href: 'history.html', permission: 'exam:review' },
    { label: 'Wrong Questions', href: 'wrong-questions.html', permission: 'wrongquestion:view' },
    { label: 'Analysis', href: 'analysis.html', permission: 'analysis:view' },
    { label: 'Generate', href: 'generate.html', permission: 'paper:create' },
    { label: 'Import Exam', href: 'import-exam.html', permission: 'exam:import' },
    { label: 'Users', href: 'users-admin.html', permission: 'user:view' },
    { label: 'Announcements', href: 'announcements-admin.html', permission: 'announcement:manage' },
    { label: 'Audit Log', href: 'audit-log.html', permission: 'audit:view' },
    { label: 'Role Permissions', href: 'role-permissions.html', permission: 'permission:manage' },
  ];

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  function currentPage() {
    return location.pathname.split('/').pop() || 'index.html';
  }

  async function render(containerId = 'sidebarRoot') {
    const root = document.getElementById(containerId);
    if (!root) return;

    const [categories, permissions] = await Promise.all([
      Api.getExamCategories().catch(() => []),
      Api.getRolePermissionsMe().catch(() => []),
    ]);

    if (categories.length && !localStorage.getItem(CATEGORY_KEY)) {
      localStorage.setItem(CATEGORY_KEY, String(categories[0].id));
    }
    const selectedCategoryId = localStorage.getItem(CATEGORY_KEY);
    const page = currentPage();

    const categoryHtml = categories.map((c) => {
      const active = page === 'index.html' && String(c.id) === selectedCategoryId;
      return `<a class="sidebar-item${active ? ' active' : ''}" href="index.html?categoryId=${c.id}" data-category-id="${c.id}">${escapeHtml(c.name)}</a>`;
    }).join('');

    const menuHtml = MENU_ITEMS
      .filter((item) => permissions.includes(item.permission))
      .map((item) => `<a class="sidebar-item${page === item.href ? ' active' : ''}" href="${item.href}">${escapeHtml(item.label)}</a>`)
      .join('');

    root.innerHTML = `
      <div class="brand">Practical Exam</div>
      <div class="sidebar-section">
        <div class="sidebar-label">Dashboard</div>
        ${categoryHtml}
      </div>
      <div class="sidebar-section">
        <div class="sidebar-label">Menu</div>
        ${menuHtml}
        <span class="sidebar-item disabled">Teaching Videos<span class="soon-tag">(coming soon)</span></span>
      </div>
    `;

    root.querySelectorAll('[data-category-id]').forEach((el) => {
      el.addEventListener('click', () => localStorage.setItem(CATEGORY_KEY, el.getAttribute('data-category-id')));
    });

    const user = Auth.getUser();
    const topBar = document.getElementById('topBarRoot');
    if (topBar) {
      topBar.innerHTML = `
        <span id="navUser">${user ? escapeHtml(user.username) : ''}</span>
        <a href="#" id="navLogout">Logout</a>
      `;
      const logoutLink = document.getElementById('navLogout');
      if (logoutLink) logoutLink.addEventListener('click', (e) => { e.preventDefault(); Auth.logout(); });
    }
  }

  return { render };
})();
