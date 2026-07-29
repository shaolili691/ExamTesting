// Shared fetch() wrappers, one per backend endpoint. Every page includes auth.js then this file.
const Api = (() => {
  function withQuery(url, params) {
    if (!params) return url;
    const entries = Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== '');
    if (entries.length === 0) return url;
    const qs = new URLSearchParams(entries.map(([k, v]) => [k, String(v)])).toString();
    return `${url}?${qs}`;
  }

  async function rawRequest(method, url, body) {
    const opts = { method, headers: {} };
    const token = Auth.getAccessToken();
    if (token) opts.headers['Authorization'] = `Bearer ${token}`;
    if (body !== undefined) {
      opts.headers['Content-Type'] = 'application/json';
      opts.body = JSON.stringify(body);
    }
    const res = await fetch(url, opts);
    const text = await res.text();
    const data = text ? JSON.parse(text) : null;
    return { res, data };
  }

  async function request(method, url, body, { retried = false } = {}) {
    const { res, data } = await rawRequest(method, url, body);

    if (res.status === 401 && !retried) {
      const refreshed = await Auth.refreshAccessToken();
      if (refreshed) return request(method, url, body, { retried: true });
      Auth.clearSession();
      location.href = 'login.html?redirect=' + encodeURIComponent(location.pathname + location.search);
      throw new Error('Session expired');
    }

    if (!res.ok) {
      const message = (data && (data.error || data.title)) || `Request failed (${res.status})`;
      const err = new Error(message);
      if (data && data.details) err.details = data.details;
      throw err;
    }
    return data;
  }

  // Multipart (file upload) variant — used only by exam import. Does NOT set Content-Type itself;
  // the browser sets it (including the multipart boundary) for FormData bodies automatically.
  async function requestMultipart(method, url, formData) {
    const opts = { method, headers: {}, body: formData };
    const token = Auth.getAccessToken();
    if (token) opts.headers['Authorization'] = `Bearer ${token}`;
    const res = await fetch(url, opts);
    const text = await res.text();
    const data = text ? JSON.parse(text) : null;
    if (!res.ok) {
      const message = (data && (data.error || data.title)) || `Request failed (${res.status})`;
      const err = new Error(message);
      if (data && data.details) err.details = data.details;
      throw err;
    }
    return data;
  }

  return {
    // Auth
    register: (username, email, password) => request('POST', '/api/auth/register', { username, email, password }),
    login: (username, password) => request('POST', '/api/auth/login', { username, password }),
    getMe: () => request('GET', '/api/auth/me'),

    // Syllabus / categories
    getSyllabusChapters: () => request('GET', '/api/syllabus-chapters'),
    getExamCategories: () => request('GET', '/api/exam-categories'),

    // Exams
    listExams: (categoryId) => request('GET', withQuery('/api/exams', { categoryId })),
    getExam: (id) => request('GET', `/api/exams/${id}`),
    generateExam: (opts) => request('POST', '/api/exams/generate', opts || {}),
    generateDrill: (attemptId, opts) => request('POST', `/api/exams/drill/${attemptId}`, opts || {}),

    // Attempts
    startAttempt: (examId) => request('POST', '/api/attempts', { examId }),
    saveAnswer: (attemptId, examQuestionId, selectedIndexes, marked) =>
      request('POST', `/api/attempts/${attemptId}/answer`, { examQuestionId, selectedIndexes, marked }),
    getAttemptState: (attemptId) => request('GET', `/api/attempts/${attemptId}/state`),
    submitAttempt: (attemptId) => request('POST', `/api/attempts/${attemptId}/submit`),
    listAttempts: () => request('GET', '/api/attempts'),
    getAttempt: (id) => request('GET', `/api/attempts/${id}`),

    // Analysis / wrong questions
    getAnalysisOverview: (params) => request('GET', withQuery('/api/analysis/overview', params)),
    getWrongQuestions: (userId) => request('GET', withQuery('/api/wrong-questions', { userId })),

    // Announcements
    listAnnouncements: (includeInactive) => request('GET', withQuery('/api/announcements', { includeInactive })),
    createAnnouncement: (body) => request('POST', '/api/announcements', body),
    updateAnnouncement: (id, body) => request('PUT', `/api/announcements/${id}`, body),

    // Users / roles (admin)
    listUsers: () => request('GET', '/api/users'),
    createUser: (body) => request('POST', '/api/users', body),
    updateUser: (id, body) => request('PUT', `/api/users/${id}`, body),
    updateMyProfile: (body) => request('PUT', '/api/users/me', body),
    listRoles: () => request('GET', '/api/users/roles'),

    // Role permissions (admin edits; every user reads their own effective set)
    getRolePermissions: () => request('GET', '/api/role-permissions'),
    getRolePermissionsMe: () => request('GET', '/api/role-permissions/me'),
    updateRolePermission: (permissionKey, roleName, allowed) =>
      request('PUT', `/api/role-permissions/${encodeURIComponent(permissionKey)}/${encodeURIComponent(roleName)}`, { allowed }),

    // Audit log (admin)
    listAuditLogs: (params) => request('GET', withQuery('/api/audit-logs', params)),

    // Exam import (Administrator/Teacher)
    previewExamImport: (questionsFile, answersFile) => {
      const fd = new FormData();
      fd.append('questionsFile', questionsFile);
      if (answersFile) fd.append('answersFile', answersFile);
      return requestMultipart('POST', '/api/exam-import/preview', fd);
    },
    confirmExamImport: (body) => request('POST', '/api/exam-import/confirm', body),
  };
})();
