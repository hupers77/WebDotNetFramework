// 스케줄 관리 (SYS_JOB_MGT): 표준 화면 3(마스터-디테일).
// 왼쪽 작업 목록에서 고르면 오른쪽에서 주기·제한 시간·사용 여부·설명을 바꾸고, 지금 실행을 요청하고, 최근 실행 이력을 본다.
// 실행은 Portal.Worker가 하므로 요청 뒤 15초쯤 지나 목록을 다시 읽으면 결과가 보인다.
// 버튼은 서버가 권한으로 그리므로(fw-button) 저장 버튼이 없으면 입력란을 읽기 전용으로 둔다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#job-screen');
const urls = { list: root.dataset.urlList, history: root.dataset.urlHistory, save: root.dataset.urlSave, run: root.dataset.urlRun };
const buttons = { save: $('#job-save'), run: $('#job-run') };
const list = $('#job-list');
const detail = $('#job-detail');
const inputs = { cron: $('#job-cron'), timeout: $('#job-timeout'), use: $('#job-use'), desc: $('#job-desc') };

let rows = [];
let current = null;

for (const input of Object.values(inputs)) {
    input.disabled = !buttons.save;
}

// 실행 상태 한 줄: 실행 중 / 실행 요청됨 / 마지막 결과
function stateText(row) {
    if (row.runningYn === 'Y') {
        return fw.t('SYS.JOB.RUNNING');
    }

    if (row.runReqYn === 'Y') {
        return fw.t('SYS.JOB.RUN_REQUESTED');
    }

    return row.lastResultNm ?? '';
}

function nextRunText(row) {
    if (row.useYn !== 'Y') {
        return fw.t('SYS.JOB.NOT_IN_USE');
    }

    if (!row.cronExpr) {
        return fw.t('SYS.JOB.MANUAL_ONLY');
    }

    return row.nextRunDt ? fw.format.dateTime(row.nextRunDt) : fw.t('SYS.JOB.NEXT_PENDING');
}

function renderList() {
    const items = rows.map((r) => {
        const li = document.createElement('li');
        li.role = 'option';
        li.tabIndex = -1;
        li.dataset.jobId = r.jobId;
        li.classList.toggle('unused', r.useYn !== 'Y');
        li.setAttribute('aria-selected', current?.jobId === r.jobId ? 'true' : 'false');
        const name = document.createElement('span');
        name.textContent = r.jobNm;
        const meta = document.createElement('span');
        meta.className = 'hint';
        meta.textContent = [r.jobId, stateText(r), nextRunText(r)].filter(Boolean).join(' · ');
        li.append(name, meta);
        return li;
    });
    list.replaceChildren(...items);
    const focusable = list.querySelector('[aria-selected="true"]') ?? list.firstElementChild;
    if (focusable) {
        focusable.tabIndex = 0;
    }
}

function cell(text) {
    const td = document.createElement('td');
    td.textContent = text ?? '';
    return td;
}

async function loadHistory(jobId) {
    const result = await fw.ajax(`${urls.history}?${new URLSearchParams({ jobId })}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    $('#hist-body').replaceChildren(...result.data.items.map((h) => {
        const tr = document.createElement('tr');
        const type = h.runUserId ? `${h.runTypeNm} (${h.runUserId})` : h.runTypeNm;
        tr.append(
            cell(fw.format.dateTime(h.startDt)),
            cell(h.endDt ? fw.format.dateTime(h.endDt) : ''),
            cell(type),
            cell(h.resultNm),
            cell(h.processedCnt == null ? '' : String(h.processedCnt)),
            cell(h.resultMsg),
            cell(h.hostNm));
        return tr;
    }));
}

function showDetail(row) {
    current = row;
    renderList();
    $('#job-hint').hidden = !!row;
    detail.hidden = !row;
    if (!row) {
        return;
    }

    const values = {
        jobId: row.jobId,
        jobNm: row.jobNm,
        target: `${row.jobTypeCd} · ${row.jobTarget}`,
        nextRun: nextRunText(row),
        lastRun: [row.lastRunDt ? fw.format.dateTime(row.lastRunDt) : '', stateText(row)].filter(Boolean).join(' · '),
    };
    for (const dd of detail.querySelectorAll('dd[data-field]')) {
        dd.textContent = values[dd.dataset.field];
    }

    inputs.cron.value = row.cronExpr ?? '';
    inputs.timeout.value = row.timeoutMin;
    inputs.use.checked = row.useYn === 'Y';
    inputs.desc.value = row.description ?? '';
    if (buttons.run) {
        buttons.run.disabled = row.useYn !== 'Y';
    }

    loadHistory(row.jobId);
}

async function load(keepJobId) {
    const query = new URLSearchParams({ keyword: $('#job-keyword').value.trim() });
    const result = await fw.ajax(`${urls.list}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    rows = result.data.items;
    $('#job-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
    showDetail(rows.find((r) => r.jobId === keepJobId) ?? null);
}

async function save() {
    const timeout = Number(inputs.timeout.value);
    if (!Number.isInteger(timeout) || timeout < 1 || timeout > 1440) {
        fw.notify(fw.t('MSG.INVALID_INPUT'));
        inputs.timeout.focus();
        return;
    }

    const body = {
        jobId: current.jobId,
        editVer: current.editVer,
        cronExpr: inputs.cron.value.trim() || null,
        timeoutMin: timeout,
        useYn: inputs.use.checked ? 'Y' : 'N',
        description: inputs.desc.value.trim() || null,
    };
    const result = await fw.ajax(urls.save, { method: 'POST', body });
    if (!result.success) {
        fw.notify(result.error.message);
        if (result.error.code === 'MSG.JOB_CRON_INVALID') {
            inputs.cron.focus();
        }

        return;
    }

    fw.notify(fw.t('MSG.SAVED'));
    await load(current.jobId);
}

async function run() {
    if (!(await fw.dialog.confirm(fw.t('CNF.JOB_RUN', current.jobNm)))) {
        return;
    }

    const result = await fw.ajax(urls.run, { method: 'POST', body: { jobId: current.jobId } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.JOB_RUN_REQUESTED', current.jobNm));
    await load(current.jobId);
}

$('#job-search').addEventListener('submit', (e) => {
    e.preventDefault();
    load();
});

$('#job-form').addEventListener('submit', (e) => {
    e.preventDefault();
    buttons.save?.click();
});

list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-job-id]');
    if (li) {
        showDetail(rows.find((r) => r.jobId === li.dataset.jobId));
    }
});

// 목록 키보드: 위/아래로 이동, Enter·Space로 선택
list.addEventListener('keydown', (e) => {
    const li = e.target.closest('li[data-job-id]');
    if (!li) {
        return;
    }

    const next = e.key === 'ArrowDown' ? li.nextElementSibling : e.key === 'ArrowUp' ? li.previousElementSibling : null;
    if (next) {
        e.preventDefault();
        li.tabIndex = -1;
        next.tabIndex = 0;
        next.focus();
    } else if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        showDetail(rows.find((r) => r.jobId === li.dataset.jobId));
        list.querySelector(`[data-job-id="${CSS.escape(li.dataset.jobId)}"]`)?.focus();
    }
});

buttons.save?.addEventListener('click', save);
buttons.run?.addEventListener('click', run);

await load();
