// 메시지 템플릿 관리 (SYS_MSG_TEMPLATE_MGT): 표준 화면 3(마스터-디테일). 템플릿은 코드가 정하므로 고치기만 한다.
// 제목·본문의 {{변수}}는 보낼 때 HTML 인코딩한 값으로 바뀐다. 본문은 서버가 스크립트·이벤트 속성을 거부한다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#tpl-screen');
const urls = { list: root.dataset.urlList, save: root.dataset.urlSave };
const langs = JSON.parse(root.dataset.langs);
const defaultLang = langs.find((l) => l.isDefault)?.langCd ?? langs[0]?.langCd;
const list = $('#tpl-list');
const save = $('#tpl-save');

let templates = [];
let current = null;
let dirty = false;

const markDirty = () => { dirty = true; };

function renderList() {
    list.replaceChildren(...templates.map((t) => {
        const li = document.createElement('li');
        li.role = 'option';
        li.tabIndex = -1;
        li.dataset.templateId = t.templateId;
        li.setAttribute('aria-selected', current?.templateId === t.templateId ? 'true' : 'false');
        const name = document.createElement('span');
        name.textContent = t.templateNm;
        const meta = document.createElement('span');
        meta.className = 'hint';
        meta.textContent = `${t.templateId}${t.useYn === 'Y' ? '' : ` · ${fw.t('LBL.NOT_IN_USE')}`}`;
        li.append(name, meta);
        if (t.useYn !== 'Y') {
            li.classList.add('unused');
        }

        return li;
    }));
    const focusable = list.querySelector('[aria-selected="true"]') ?? list.firstElementChild;
    if (focusable) {
        focusable.tabIndex = 0;
    }
}

async function load() {
    const keyword = $('#tpl-keyword').value.trim();
    const result = await fw.ajax(`${urls.list}${keyword ? `?keyword=${encodeURIComponent(keyword)}` : ''}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    templates = result.data;
    $('#tpl-total').textContent = fw.t('LBL.TOTAL', templates.length);
    current = templates.find((t) => t.templateId === current?.templateId) ?? null;
    renderList();
    if (current) {
        show(current);
    } else {
        $('#tpl-detail').hidden = true;
        $('#tpl-hint').hidden = false;
    }
}

function field(name) {
    return $(`#tpl-detail [data-field="${name}"]`);
}

function show(template) {
    current = template;
    dirty = false;
    $('#tpl-hint').hidden = true;
    $('#tpl-detail').hidden = false;
    $('#tpl-heading').textContent = template.templateNm;
    field('id').textContent = template.templateId;
    field('link').textContent = template.linkProgramId ?? '-';
    field('pending').textContent = String(template.pendingCnt);
    field('upd').textContent = `${fw.format.dateTime(template.updDt)} (${template.updId})`;
    $('#tpl-mail').checked = template.mailYn === 'Y';
    $('#tpl-portal').checked = template.portalYn === 'Y';
    $('#tpl-use').checked = template.useYn === 'Y';

    const byLang = new Map(template.langs.map((l) => [l.langCd, l]));
    $('#tpl-langs').replaceChildren(...langs.map((l) => {
        const text = byLang.get(l.langCd);
        const set = document.createElement('fieldset');
        set.dataset.langCd = l.langCd;
        const legend = document.createElement('legend');
        legend.textContent = `${l.langNm}${l.isDefault ? ` (${fw.t('LBL.DEFAULT')})` : ''}`;
        const subjectLabel = document.createElement('label');
        subjectLabel.textContent = fw.t('SYS.TPL.SUBJECT');
        const subject = document.createElement('input');
        subject.maxLength = 200;
        subject.value = text?.subject ?? '';
        subject.id = `tpl-subject-${l.langCd}`;
        subjectLabel.htmlFor = subject.id;
        subject.required = l.isDefault;
        const bodyLabel = document.createElement('label');
        bodyLabel.textContent = fw.t('SYS.TPL.BODY');
        const body = document.createElement('textarea');
        body.maxLength = 4000;
        body.rows = 8;
        body.value = text?.body ?? '';
        body.id = `tpl-body-${l.langCd}`;
        bodyLabel.htmlFor = body.id;
        body.required = l.isDefault;
        subject.addEventListener('input', markDirty);
        body.addEventListener('input', markDirty);
        set.append(legend, subjectLabel, subject, bodyLabel, body);
        return set;
    }));
    renderList();
}

async function select(templateId) {
    const next = templates.find((t) => t.templateId === templateId);
    if (!next || next.templateId === current?.templateId) {
        return;
    }

    if (dirty && !(await fw.dialog.confirm(fw.t('CNF.LEAVE')))) {
        return;
    }

    show(next);
}

function collect() {
    const items = [];
    for (const set of $('#tpl-langs').querySelectorAll('fieldset')) {
        const subject = set.querySelector('input').value.trim();
        const body = set.querySelector('textarea').value.trim();
        if (subject || body) {
            if (!subject || !body) {
                return { error: fw.t('SYS.TPL.BOTH_REQUIRED') };
            }

            items.push({ langCd: set.dataset.langCd, subject, body });
        }
    }

    if (!items.some((i) => i.langCd === defaultLang)) {
        return { error: fw.t('SYS.TPL.DEFAULT_REQUIRED') };
    }

    return { items };
}

async function submit() {
    const { items, error } = collect();
    if (error) {
        fw.notify(error);
        return;
    }

    if (!$('#tpl-mail').checked && !$('#tpl-portal').checked) {
        fw.notify(fw.t('SYS.TPL.CHANNEL_REQUIRED'));
        return;
    }

    if (!(await fw.dialog.confirm(fw.t('CNF.SAVE')))) {
        return;
    }

    save.disabled = true;
    try {
        const result = await fw.ajax(urls.save, {
            method: 'POST',
            body: {
                templateId: current.templateId,
                rowVer: current.rowVer,
                mailYn: $('#tpl-mail').checked ? 'Y' : 'N',
                portalYn: $('#tpl-portal').checked ? 'Y' : 'N',
                useYn: $('#tpl-use').checked ? 'Y' : 'N',
                langs: items,
            },
        });
        if (!result.success) {
            fw.notify(result.error.message);
            return;
        }

        dirty = false;
        await load();
        fw.notify(fw.t('MSG.SAVED'));
    } finally {
        save.disabled = false;
    }
}

$('#tpl-search').addEventListener('submit', (e) => {
    e.preventDefault();
    load();
});
list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-template-id]');
    if (li) {
        select(li.dataset.templateId);
    }
});
list.addEventListener('keydown', (e) => {
    const li = e.target.closest('li[data-template-id]');
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
        select(li.dataset.templateId);
    }
});
for (const id of ['#tpl-mail', '#tpl-portal', '#tpl-use']) {
    $(id).addEventListener('change', markDirty);
}

save?.addEventListener('click', submit);
window.addEventListener('beforeunload', (e) => {
    if (dirty) {
        e.preventDefault();
    }
});

await load();
