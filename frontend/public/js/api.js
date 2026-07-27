// Shared fetch() wrappers, one per backend endpoint. Every page includes this file.
const Api = (() => {
  async function request(method, url, body) {
    const opts = { method, headers: {} };
    if (body !== undefined) {
      opts.headers['Content-Type'] = 'application/json';
      opts.body = JSON.stringify(body);
    }
    const res = await fetch(url, opts);
    const text = await res.text();
    const data = text ? JSON.parse(text) : null;
    if (!res.ok) {
      const message = (data && (data.error || data.title)) || `Request failed (${res.status})`;
      throw new Error(message);
    }
    return data;
  }

  return {
    getSyllabusChapters: () => request('GET', '/api/syllabus-chapters'),
    listExams: () => request('GET', '/api/exams'),
    getExam: (id) => request('GET', `/api/exams/${id}`),
    generateExam: (opts) => request('POST', '/api/exams/generate', opts || {}),
    generateDrill: (attemptId, opts) => request('POST', `/api/exams/drill/${attemptId}`, opts || {}),
    startAttempt: (examId) => request('POST', '/api/attempts', { examId }),
    submitAttempt: (attemptId, answers) => request('POST', `/api/attempts/${attemptId}/submit`, { answers }),
    listAttempts: () => request('GET', '/api/attempts'),
    getAttempt: (id) => request('GET', `/api/attempts/${id}`),
    getAnalysisOverview: () => request('GET', '/api/analysis/overview'),
  };
})();
