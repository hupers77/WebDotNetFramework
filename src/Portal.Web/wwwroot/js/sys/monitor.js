// 운영 모니터링 (SYS_MONITOR_INQ): 읽기 전용 현황. 30초마다 다시 읽되 사용자가 한 요청이 아니므로
// 세션을 연장하지 않는다(X-Portal-Background). 값은 textContent로만 넣는다(innerHTML 금지).
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const url = $('#mon-screen').dataset.urlData;
const REFRESH_MS = 30000;

let timer = null;

function cell(tag, text, className) {
    const el = document.createElement(tag);
    el.textContent = text ?? '';
    if (className) {
        el.className = className;
    }

    return el;
}

function table(target, headers, rows, emptyKey) {
    const head = document.createElement('thead');
    const headRow = document.createElement('tr');
    headRow.append(...headers.map((h) => Object.assign(cell('th', h), { scope: 'col' })));
    head.append(headRow);
    const body = document.createElement('tbody');
    if (rows.length === 0) {
        const tr = document.createElement('tr');
        const td = cell('td', fw.t(emptyKey ?? 'LBL.NO_DATA'));
        td.colSpan = headers.length;
        tr.append(td);
        body.append(tr);
    }

    for (const row of rows) {
        const tr = document.createElement('tr');
        tr.append(...row.map((value) => (value instanceof Node ? value : cell('td', value))));
        body.append(tr);
    }

    target.replaceChildren(head, body);
}

function list(target, entries) {
    target.replaceChildren(...entries.flatMap(([label, value, warn]) => [
        cell('dt', label),
        cell('dd', value, warn ? 'warn' : ''),
    ]));
}

const dt = (value) => (value ? fw.format.dateTime(value) : '-');
const flag = (text, warn) => cell('td', text, warn ? 'warn' : '');

function render(data) {
    const s = data.summary;
    list($('#mon-summary'), [
        [fw.t('SYS.MON.DB_TIME'), dt(s.dbDt)],
        [fw.t('SYS.MON.SESSIONS'), String(s.sessionCnt)],
        [fw.t('SYS.MON.LOCKED'), String(s.lockedCnt), s.lockedCnt > 0],
        [fw.t('SYS.MON.LOGIN_FAIL'), String(s.loginFailCnt), s.loginFailCnt >= 50],
        [fw.t('SYS.MON.ERROR_CNT'), String(s.errorCnt), s.errorCnt > 0],
    ]);

    const v = data.server;
    list($('#mon-server'), [
        [fw.t('SYS.MON.VERSION'), v.version],
        [fw.t('SYS.MON.RUNTIME'), v.runtime],
        [fw.t('SYS.MON.MACHINE'), v.machineName],
        [fw.t('SYS.MON.STARTED'), dt(v.startedDt)],
        [fw.t('SYS.MON.MEMORY'), `${v.workingSetMb} MB / ${v.managedMb} MB`],
        [fw.t('SYS.MON.THREADS'), String(v.threadCount)],
        [fw.t('SYS.MON.GEN2'), String(v.gen2Collections)],
    ]);

    table($('#mon-jobs'),
        [fw.t('SYS.JOB.ID'), fw.t('SYS.JOB.NAME'), fw.t('LBL.USE_YN'), fw.t('SYS.MON.JOB_STATE'), fw.t('SYS.MON.LAST_RUN'), fw.t('SYS.MON.LAST_RESULT'), fw.t('SYS.MON.NEXT_RUN')],
        data.jobs.map((j) => [
            j.jobId, j.jobNm, j.useYn === 'Y' ? '✓' : '',
            flag(j.stuckYn === 'Y' ? fw.t('SYS.MON.STUCK') : j.overdueYn === 'Y' ? fw.t('SYS.MON.OVERDUE') : j.runningYn === 'Y' ? fw.t('SYS.MON.RUNNING') : '-',
                j.stuckYn === 'Y' || j.overdueYn === 'Y'),
            dt(j.lastRunDt), flag(j.lastResultNm ?? '-', j.lastResultCd === 'FAILED'), dt(j.nextRunDt),
        ]));

    const queueHeaders = [fw.t('SYS.MON.STATUS'), fw.t('SYS.MON.COUNT'), fw.t('SYS.MON.OLDEST')];
    table($('#mon-mails'), queueHeaders, data.mails.map((m) => [flag(`${fw.t('SYS.MON.MAILS')} · ${m.statCd}`, m.statCd === 'FAILED'), String(m.cnt), dt(m.oldestDt)]));
    table($('#mon-reports'), queueHeaders, data.reports.map((r) => [flag(`${fw.t('SYS.MON.REPORTS')} · ${r.statCd}`, r.statCd === 'FAILED'), String(r.cnt), dt(r.oldestDt)]));

    table($('#mon-caches'), [fw.t('SYS.MON.CACHE_KEY'), fw.t('SYS.MON.CACHE_VER'), fw.t('SYS.MON.CACHE_UPD')],
        data.caches.map((c) => [c.cacheKey, String(c.versionNo), dt(c.updDt)]));

    table($('#mon-errors'),
        [fw.t('LBL.TIME'), fw.t('SYS.MON.TRACE'), fw.t('LBL.USER_ID'), fw.t('SYS.MON.PATH'), fw.t('SYS.MON.SOURCE'), fw.t('LBL.CONTENT')],
        data.errors.map((e) => [dt(e.logDt), e.traceId ?? '-', e.userId ?? '-', e.requestPath ?? '-', e.source ?? '-', e.message]));

    $('#mon-updated').textContent = fw.t('SYS.MON.UPDATED', fw.format.dateTime(new Date().toISOString()));
}

async function load() {
    try {
        const response = await fetch(url, {
            headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json', 'X-Portal-Background': '1' },
            credentials: 'same-origin',
        });
        const result = await response.json();
        if (!result.success) {
            fw.notify(result.error.message);
            return;
        }

        render(result.data);
    } catch {
        // 연결이 잠시 끊겼다. 다음 주기나 새로고침 버튼으로 다시 읽는다
    }
}

function schedule() {
    clearInterval(timer);
    timer = $('#mon-auto').checked ? setInterval(load, REFRESH_MS) : null;
}

$('#mon-refresh').addEventListener('click', load);
$('#mon-auto').addEventListener('change', schedule);
document.addEventListener('visibilitychange', () => {
    if (!document.hidden && $('#mon-auto').checked) {
        load();
    }
});

await load();
schedule();
