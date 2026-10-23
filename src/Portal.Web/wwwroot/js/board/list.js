// 게시판 목록 (BRD_*): 표준 화면 1(조회). 검색 대상·검색어와 쪽 넘김. 제목을 누르면 글 보기로 간다(같은 탭 안에서 이동).
// 비밀글은 볼 수 없는 사람에게 제목이 오지 않는다(안내 문구로 바꿔 보여 준다). 답글은 단계만큼 들여쓴다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#brd-screen');
const urls = { list: root.dataset.urlList, view: root.dataset.urlView };
const COLUMNS = 4;

let page = { items: [], pageNo: 1, totalPages: 0, totalCount: 0 };
let searched = { searchType: 'TITLE_CONTENT', keyword: '' };

function cell(text, className) {
    const td = document.createElement('td');
    td.textContent = text ?? '';
    if (className) {
        td.className = className;
    }

    return td;
}

function badge(text) {
    const span = document.createElement('span');
    span.className = 'badge';
    span.textContent = text;
    return span;
}

function titleCell(row) {
    const td = document.createElement('td');
    td.className = `board-title depth-${row.depth}`;
    if (row.pinYn === 'Y' && row.depth === 0) {
        td.append(badge(fw.t('BRD.PINNED')));
    }

    if (row.depth > 0) {
        td.append(badge(fw.t('BRD.REPLY')));
    }

    if (row.secretYn === 'Y') {
        td.append(badge(fw.t('BRD.SECRET')));
    }

    const link = document.createElement('a');
    link.href = `${urls.view}${row.postId}`;
    link.textContent = row.title ?? fw.t('BRD.SECRET_TITLE');
    td.append(link);
    if (row.commentCnt > 0) {
        td.append(` [${row.commentCnt}]`);
    }

    if (row.hasFileYn === 'Y') {
        td.append(' ', badge(fw.t('BRD.HAS_FILE')));
    }

    return td;
}

function render() {
    const rows = page.items.map((r) => {
        const tr = document.createElement('tr');
        tr.classList.toggle('pinned', r.pinYn === 'Y' && r.depth === 0);
        tr.append(titleCell(r), cell(r.writerNm ?? fw.t('BRD.ANONYMOUS')), cell(fw.format.dateTime(r.regDt)), cell(r.viewCnt));
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
    $('#brd-pager').hidden = page.totalPages <= 1;
    $('#brd-page').textContent = page.totalPages > 1 ? fw.t('MY.NOTI.PAGE', page.pageNo, page.totalPages) : '';
    $('#brd-prev').disabled = page.pageNo <= 1;
    $('#brd-next').disabled = page.pageNo >= page.totalPages;
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
$('#brd-prev').addEventListener('click', () => load(page.pageNo - 1));
$('#brd-next').addEventListener('click', () => load(page.pageNo + 1));

load(1);
