// 내 정보 > 내 레포트 (RPT_RUNS): 백그라운드로 요청한 레포트의 진행과 결과 파일.
// 대기·생성 중인 요청이 있으면 5초마다 다시 읽는다(사용자가 한 요청이 아니므로 세션을 연장하지 않는다: X-Portal-Background).
// 끝난 요청은 내려받거나 지운다. 문구는 글자로만 넣는다(textContent).
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#runs-screen');
const urls = { list: root.dataset.urlList, download: root.dataset.urlDownload, delete: root.dataset.urlDelete };
const body = $('#runs-body');
const POLL_MS = 5000;

let items = [];
let timer = null;

const isActive = (run) => run.statusCd === 'QUEUED' || run.statusCd === 'RUNNING';

function cell(text, className) {
    const td = document.createElement('td');
    td.textContent = text ?? '';
    if (className) {
        td.className = className;
    }

    return td;
}

function actionButton(action, text) {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.action = action;
    button.textContent = text;
    return button;
}

function statusText(run) {
    const text = fw.t(`RPT.RUN_STATUS.${run.statusCd}`);
    return run.statusCd === 'FAILED' && run.errMsg ? `${text}: ${run.errMsg}` : text;
}

function render() {
    const rows = items.map((run) => {
        const tr = document.createElement('tr');
        tr.dataset.runId = run.runId;
        tr.dataset.status = run.statusCd;

        const actions = document.createElement('td');
        if (run.statusCd === 'DONE') {
            actions.append(actionButton('download', fw.t('BTN.DOWNLOAD')));
        }

        if (!isActive(run) || run.statusCd === 'QUEUED') {
            actions.append(actionButton('delete', fw.t('BTN.DELETE')));
        }

        tr.append(
            cell(run.reportNm),
            cell(run.formatCd === 'PDF' ? 'PDF' : 'Excel'),
            cell(statusText(run)),
            cell(run.rowCnt === null || run.rowCnt === undefined ? '' : String(run.rowCnt), 'num'),
            cell(run.fileSize ? fw.format.fileSize(run.fileSize) : '', 'num'),
            cell(fw.format.dateTime(run.regDt)),
            cell(fw.format.dateTime(run.expireDt)),
            actions,
        );
        return tr;
    });
    body.replaceChildren(...rows);
    $('#runs-empty').hidden = rows.length > 0;
    $('#runs-total').textContent = fw.t('LBL.TOTAL', rows.length);
}

function schedule() {
    clearTimeout(timer);
    if (items.some(isActive)) {
        timer = setTimeout(poll, POLL_MS);
    }
}

async function load() {
    const result = await fw.ajax(urls.list);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    items = result.data;
    render();
    schedule();
}

async function poll() {
    try {
        const response = await fetch(urls.list, {
            headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json', 'X-Portal-Background': '1' },
            credentials: 'same-origin',
        });
        if (!response.ok) {
            return;
        }

        const result = await response.json();
        if (result.success) {
            items = result.data;
            render();
            schedule();
        }
    } catch {
        // 연결이 잠시 끊겼다. 사용자가 새로고침하면 다시 읽는다
    }
}

async function download(runId) {
    const result = await fw.download(urls.download, { runId });
    if (!result.success) {
        fw.notify(result.error.message);
    }
}

async function remove(runId) {
    if (!(await fw.dialog.confirm(fw.t('CNF.RPT_RUN_DELETE')))) {
        return;
    }

    const result = await fw.ajax(urls.delete, { method: 'POST', body: { runId } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.DELETED'));
    await load();
}

body.addEventListener('click', (e) => {
    const button = e.target.closest('button[data-action]');
    if (!button) {
        return;
    }

    const runId = Number(button.closest('tr').dataset.runId);
    if (button.dataset.action === 'download') {
        download(runId);
    } else {
        remove(runId);
    }
});

$('#runs-refresh').addEventListener('click', load);

await load();
