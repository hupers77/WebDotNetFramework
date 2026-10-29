// 게시판 목록 (BRD_*): 표준 화면 1(조회). 검색 대상·검색어와 쪽 번호. 제목을 누르면 글 보기로 간다(같은 탭 안에서 이동).
// 비밀글은 볼 수 없는 사람에게 제목이 오지 않는다(안내 문구로 바꿔 보여 준다). 답글은 단계만큼 들여쓴다.
// 하루 안에 쓴 글은 N 표시, 일시는 오늘이면 시:분만 보인다(마우스를 올리면 전체 일시).
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#brd-screen');
const urls = { list: root.dataset.urlList, view: root.dataset.urlView };
const COLUMNS = 4;

let page = { items: [], pageNo: 1, totalPages: 0, totalCount: 0 };
let searched = { searchType: 'TITLE_CONTENT', keyword: '' };

const PAGE_WINDOW = 5;

function cell(text, className, label) {
    const td = document.createElement('td');
    td.textContent = text ?? '';
    if (className) {
        td.className = className;
    }

    if (label) {
        td.dataset.label = label;
    }

    return td;
}

function badge(text, iconName, className) {
    const span = document.createElement('span');
    span.className = className ? `badge ${className}` : 'badge';
    if (iconName) {
        span.append(fw.icon(iconName));
    }

    span.append(text);
    return span;
}

function metaIcon(name, label) {
    const svg = fw.icon(name, 'meta-ic');
    const wrap = document.createElement('span');
    wrap.append(svg);
    wrap.title = label;
    const sr = document.createElement('span');
    sr.className = 'sr-only';
    sr.textContent = label;
    wrap.append(sr);
    return wrap;
}

function titleCell(row) {
    const td = document.createElement('td');
    td.className = `board-title depth-${row.depth}`;
    if (row.pinYn === 'Y' && row.depth === 0) {
        td.append(badge(fw.t('BRD.PINNED'), 'pin', 'badge-pin'));
    }

    if (row.depth > 0) {
        td.append(badge(fw.t('BRD.REPLY'), 'reply'));
    }

    if (row.secretYn === 'Y') {
        td.append(badge(fw.t('BRD.SECRET'), 'lock'));
    }

    const link = document.createElement('a');
    link.href = `${urls.view}${row.postId}`;
    link.textContent = row.title ?? fw.t('BRD.SECRET_TITLE');
    td.append(link);
    if (row.commentCnt > 0) {
        const count = document.createElement('span');
        count.className = 'comment-cnt';
        count.textContent = `[${row.commentCnt}]`;
        count.title = fw.t('BRD.COMMENTS');
        td.append(count);
    }

    if (fw.format.isRecent(row.regDt, 24)) {
        td.append(badge('N', undefined, 'badge-new'));
        td.lastChild.title = fw.t('BRD.NEW');
    }

    if (row.hasFileYn === 'Y') {
        td.append(metaIcon('paperclip', fw.t('BRD.HAS_FILE')));
    }

    return td;
}

function render() {
    const rows = page.items.map((r) => {
        const tr = document.createElement('tr');
        tr.classList.toggle('pinned', r.pinYn === 'Y' && r.depth === 0);
        const date = cell(fw.format.shortDate(r.regDt), 'col-meta');
        date.title = fw.format.dateTime(r.regDt);
        tr.append(titleCell(r), cell(r.writerNm ?? fw.t('BRD.ANONYMOUS'), 'col-meta'), date, cell(r.viewCnt, 'col-num', fw.t('BRD.VIEW_CNT')));
        return tr;
    });
    if (rows.length === 0) {
        const tr = document.createElement('tr');
        const td = cell(fw.t('BRD.EMPTY'), 'grid-empty');
        td.colSpan = COLUMNS;
        tr.append(td);
        rows.push(tr);
    }

    $('#brd-body').replaceChildren(...rows);
    $('#brd-total').textContent = fw.t('LBL.TOTAL', page.totalCount);
    renderPager();
}

/** 쪽 번호: 이전, 현재 쪽 둘레 PAGE_WINDOW개, 다음. 처음·끝 쪽은 항상 보인다. */
function renderPager() {
    const pager = $('#brd-pager');
    pager.hidden = page.totalPages <= 1;
    if (pager.hidden) {
        pager.replaceChildren();
        return;
    }

    const button = (pageNo, content, label) => {
        const b = document.createElement('button');
        b.type = 'button';
        b.append(content);
        b.setAttribute('aria-label', label ?? fw.t('LBL.PAGE_NO', pageNo));
        if (pageNo === page.pageNo) {
            b.setAttribute('aria-current', 'page');
        }

        b.disabled = pageNo < 1 || pageNo > page.totalPages;
        b.addEventListener('click', () => load(pageNo));
        return b;
    };
    const gap = () => {
        const span = document.createElement('span');
        span.className = 'pager-gap';
        span.textContent = '…';
        return span;
    };
    const from = Math.max(1, Math.min(page.pageNo - Math.floor(PAGE_WINDOW / 2), page.totalPages - PAGE_WINDOW + 1));
    const to = Math.min(page.totalPages, from + PAGE_WINDOW - 1);
    const items = [button(page.pageNo - 1, fw.icon('chevron-left'), fw.t('BTN.PREV'))];
    if (from > 1) {
        items.push(button(1, '1'));
        if (from > 2) {
            items.push(gap());
        }
    }

    for (let n = from; n <= to; n++) {
        items.push(button(n, String(n)));
    }

    if (to < page.totalPages) {
        if (to < page.totalPages - 1) {
            items.push(gap());
        }

        items.push(button(page.totalPages, String(page.totalPages)));
    }

    items.push(button(page.pageNo + 1, fw.icon('chevron-right'), fw.t('BTN.NEXT')));
    pager.replaceChildren(...items);
}

async function load(pageNo) {
    const query = new URLSearchParams({ searchType: searched.searchType, pageNo, pageSize: 20 });
    if (searched.keyword) {
        query.set('keyword', searched.keyword);
    }

    const result = await fw.ajax(`${urls.list}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    page = result.data;
    render();
}

$('#brd-search').addEventListener('submit', (e) => {
    e.preventDefault();
    searched = { searchType: $('#brd-type').value, keyword: $('#brd-keyword').value.trim() };
    load(1);
});

load(1);
