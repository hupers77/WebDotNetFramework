// 다국어 문구 관리 (SYS_LANG_MGT): 표준 화면 2(그리드 편집형). 키 하나에 사용 중인 모든 언어의 문구를 한 행으로 편집한다.
// 키는 코드가 쓰므로 만든 뒤 바꿀 수 없고, 저장하면 문구 캐시(LANG)가 바뀌어 웹서버가 5초 안에 새 문구를 쓴다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#lang-screen');
const urls = { list: root.dataset.urlList, save: root.dataset.urlSave, export: root.dataset.urlExport, upload: root.dataset.urlUpload };
const langs = JSON.parse(root.dataset.langs);
const buttons = {
    add: $('#lang-add'), del: $('#lang-delete'), save: $('#lang-save'), export: $('#lang-export'), upload: $('#lang-upload'),
};
const KEY_PATTERN = /^[A-Z0-9_.]{3,100}$/;
const TYPES = ['LABEL', 'MSG', 'BTN', 'TOOLTIP'];
const defaultLang = langs.find((l) => l.isDefault)?.langCd ?? langs[0]?.langCd;

let pageNo = 1;
let totalPages = 1;
let query = '';

const grid = new Grid($('#lang-grid'), {
    label: fw.t('SYS.LANG.TITLE'),
    editable: !!buttons.save,
    columns: [
        {
            field: 'resKey', header: fw.t('SYS.LANG.KEY'), required: true, maxLength: 100, width: '18rem',
            readonly: (row, state) => state !== 'I',
            check: (row) => (KEY_PATTERN.test(row.resKey ?? '') && row.resKey.includes('.') && !row.resKey.startsWith('.')
                && !row.resKey.endsWith('.') && !row.resKey.includes('..') ? null : fw.t('SYS.LANG.KEY_RULE')),
        },
        {
            field: 'resType', header: fw.t('SYS.LANG.TYPE'), required: true, maxLength: 10, width: '7rem',
            check: (row) => (TYPES.includes(row.resType) ? null : fw.t('SYS.LANG.TYPE_HINT', TYPES.join('/'))),
        },
        ...langs.map((l) => ({
            field: `texts.${l.langCd}`, header: fw.t('SYS.LANG.TEXT_IN', l.langNm), required: l.langCd === defaultLang, maxLength: 1000, width: '20rem',
        })),
    ],
});

function params() {
    const search = new URLSearchParams();
    const keyword = $('#lang-keyword').value.trim();
    if (keyword) {
        search.set('keyword', keyword);
    }

    if ($('#lang-type').value) {
        search.set('resType', $('#lang-type').value);
    }

    if ($('#lang-missing').checked) {
        search.set('missingYn', 'Y');
    }

    return search.toString();
}

async function load() {
    const search = new URLSearchParams(query);
    search.set('pageNo', String(pageNo));
    const result = await fw.ajax(`${urls.list}?${search}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    const { items, totalCount, totalPages: pages } = result.data;
    totalPages = Math.max(pages, 1);
    $('#lang-total').textContent = fw.t('LBL.TOTAL', totalCount);
    $('#lang-page').textContent = `${pageNo} / ${totalPages}`;
    $('#lang-pager').hidden = totalPages <= 1;
    $('#lang-prev').disabled = pageNo <= 1;
    $('#lang-next').disabled = pageNo >= totalPages;
    showUploadErrors([]);
    grid.setRows(items);
}

async function leaveOk() {
    return !grid.hasChanges() || fw.dialog.confirm(fw.t('CNF.LEAVE'));
}

async function save() {
    if (!grid.hasChanges()) {
        fw.notify(fw.t('MSG.NO_CHANGES'));
        return;
    }

    const error = grid.validate();
    if (error) {
        fw.notify(error);
        return;
    }

    if (!(await fw.dialog.confirm(fw.t('CNF.SAVE')))) {
        return;
    }

    buttons.save.disabled = true;
    try {
        const rows = grid.getChanges().map((r) => ({
            rowStat: r.rowStat, resKey: r.resKey, resType: r.resType, texts: r.texts ?? {}, rowVer: r.rowVer,
        }));
        const result = await fw.ajax(urls.save, { method: 'POST', body: { rows } });
        if (!result.success) {
            fw.notify(result.error.message);
            return;
        }

        await load();
        fw.notify(fw.t('MSG.SAVED'));
    } finally {
        buttons.save.disabled = false;
    }
}

$('#lang-search').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!(await leaveOk())) {
        return;
    }

    query = params();
    pageNo = 1;
    load();
});

$('#lang-prev').addEventListener('click', async () => {
    if (pageNo > 1 && (await leaveOk())) {
        pageNo -= 1;
        load();
    }
});

$('#lang-next').addEventListener('click', async () => {
    if (pageNo < totalPages && (await leaveOk())) {
        pageNo += 1;
        load();
    }
});

buttons.add?.addEventListener('click', () => grid.addRow({ resKey: '', resType: 'LABEL', texts: {} }));
buttons.del?.addEventListener('click', async () => {
    if (grid.selectedStates().length === 0) {
        fw.notify(fw.t('MSG.NO_ROW_SELECTED'));
        return;
    }

    // 코드가 쓰는 키를 지우면 화면에 키가 그대로 보이므로 한 번 더 확인한다
    if (grid.selectedStates().some((state) => state !== 'I') && !(await fw.dialog.confirm(fw.t('SYS.LANG.DELETE_CONFIRM')))) {
        return;
    }

    grid.deleteSelected();
});
buttons.save?.addEventListener('click', save);

// 엑셀: 첨부 응답이라 화면은 그대로 두고 브라우저가 내려받는다 (현재 검색 조건 전체)
buttons.export?.addEventListener('click', () => {
    window.location.assign(`${urls.export}${query ? `?${query}` : ''}`);
});

// 엑셀 업로드: 서버가 양식·값을 검사한 행만 그리드에 반영하고, 저장은 사용자가 저장 버튼으로 한다
const uploadInput = $('#lang-upload-file');
const uploadErrors = $('#lang-upload-errors');

function showUploadErrors(errors) {
    uploadErrors.hidden = errors.length === 0;
    uploadErrors.querySelector('ul').replaceChildren(...errors.map((e) => {
        const li = document.createElement('li');
        li.textContent = fw.t('LBL.EXCEL_ROW_ERROR', e.rowNo, e.message);
        return li;
    }));
}

buttons.upload?.addEventListener('click', () => uploadInput.click());
uploadInput.addEventListener('change', async () => {
    const file = uploadInput.files[0];
    uploadInput.value = '';
    if (!file) {
        return;
    }

    buttons.upload.disabled = true;
    try {
        const result = await fw.upload(urls.upload, file);
        if (!result.success) {
            showUploadErrors([]);
            fw.notify(result.error.message);
            return;
        }

        const { rows, errors, unchangedCnt } = result.data;
        grid.applyChanges(rows, 'resKey');
        showUploadErrors(errors);
        fw.notify(fw.t('MSG.EXCEL_APPLIED', rows.length, unchangedCnt, errors.length));
    } finally {
        buttons.upload.disabled = false;
    }
});

window.addEventListener('beforeunload', (e) => {
    if (grid.hasChanges()) {
        e.preventDefault();
    }
});

await load();
