// 화면 공통 스크립트 (개발 표준 가이드 5장). 서버 호출은 fw.ajax만 쓴다: CSRF 헤더와 AJAX 표시를 자동으로 붙이고,
// 실패 응답은 { success:false, error:{ code, message }, traceId } 그대로 돌려준다. 로딩·다이얼로그·그리드는 2단계에서 추가한다.
const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content ?? '';

async function ajax(url, { method = 'GET', body } = {}) {
    const headers = { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' };
    if (method !== 'GET') {
        headers['X-CSRF-TOKEN'] = csrf();
        headers['Content-Type'] = 'application/json';
    }

    const response = await fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), credentials: 'same-origin' });
    if (response.status === 401) {
        window.location.assign('/Account/Login?returnUrl=' + encodeURIComponent(window.location.pathname));
    }

    return response.json();
}

export const fw = { ajax };
window.fw = fw;
