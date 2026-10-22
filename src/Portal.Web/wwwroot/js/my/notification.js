// 내 정보 > 알림함 (MY_NOTIFICATION): 표준 화면 1(목록).
// 제목을 누르면 읽음으로 표시하고, 연결된 화면이 있으면 새 탭으로 연다. 바뀌면 헤더 배지를 다시 센다.
// 삭제 버튼은 서버가 권한으로 그린다(fw-button). 알림 문구는 글자로만 넣는다(textContent).
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#noti-screen');
const urls = { list: root.dataset.urlList, read: root.dataset.urlRead, delete: root.dataset.urlDelete };
const body = $('#noti-body');
const checkAll = $('#noti-check-all');
const deleteButton = $('#noti-delete');
const shell = window.top.fwShell;

let page = { items: [], pageNo: 1, totalPages: 0 };

function refreshBadge() {
    shell?.refreshNotifications?.();
}

function cell(className) {
    const td = document.createElement('td');
    if (className) {
        td.className = className;
    }

    return td;
}

function render() {
    const rows = page.items.map((n) => {
        const tr = document.createElement('tr');
        tr.dataset.notiId = n.notiId;
        tr.classList.toggle('unread', n.readYn !== 'Y');

        const check = cell('col-check');
        const box = document.createElement('input');
        box.type = 'checkbox';
        box.value = n.notiId;
        box.setAttribute('aria-label', `${fw.t('LBL.ROW_SELECT')} ${n.title}`);
        box.hidden = !deleteButton;
        check.append(box);

        const title = cell('noti-title');
        if (n.readYn !== 'Y') {
            const mark = document.createElement('span');
            mark.className = 'noti-unread-mark';
            mark.textContent = fw.t('LBL.UNREAD');
            title.append(mark);
        }

        const open = document.createElement('button');
        open.type = 'button';
        open.dataset.action = 'open';
        open.textContent = n.title;
        if (n.linkUrl) {
            open.title = fw.t('BTN.OPEN_SCREEN');
        }

        title.append(open);

        const content = cell('noti-content');
        content.textContent = n.content;
        const date = cell();
        date.textContent = fw.format.dateTime(n.regDt);
        tr.append(check, title, content, date);
        return tr;
    });
    body.replaceChildren(...rows);
    checkAll.checked = false;
    checkAll.hidden = !deleteButton;
    $('#noti-empty').hidden = rows.length > 0;
    $('#noti-page').textContent = page.totalPages > 1 ? fw.t('MY.NOTI.PAGE', page.pageNo, page.totalPages) : '';
    $('#noti-prev').disabled = page.pageNo <= 1;
    $('#noti-next').disabled = page.pageNo >= page.totalPages;
}

async function load(pageNo = 1) {
    const query = new URLSearchParams({ unreadYn: $('#noti-unread').checked ? 'Y' : 'N', pageNo });
    const result = await fw.ajax(`${urls.list}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    // 마지막 쪽을 다 지웠으면 앞 쪽을 보여 준다
    if (result.data.items.length === 0 && pageNo > 1) {
        await load(pageNo - 1);
        return;
    }

    page = result.data;
    $('#noti-total').textContent = fw.t('LBL.TOTAL', page.totalCount);
    render();
}

async function markRead(notiId) {
    const result = await fw.ajax(urls.read, { method: 'POST', body: { notiId } });
    if (!result.success) {
        fw.notify(result.error.message);
        return false;
    }

    refreshBadge();
    return true;
}

async function open(notiId) {
    const item = page.items.find((n) => String(n.notiId) === String(notiId));
    if (!item) {
        return;
    }

    if (item.readYn !== 'Y' && await markRead(item.notiId)) {
        item.readYn = 'Y';
        if ($('#noti-unread').checked) {
            await load(page.pageNo);
        } else {
            render();
        }
    }

    if (item.linkUrl) {
        if (shell?.openUrl) {
            shell.openUrl(item.linkUrl, item.title);
        } else {
            window.location.assign(item.linkUrl);
        }
    }
}

function selectedIds() {
    return [...body.querySelectorAll('input[type="checkbox"]:checked')].map((c) => Number(c.value));
}

async function remove() {
    const ids = selectedIds();
    if (ids.length === 0) {
        fw.notify(fw.t('MSG.NO_ROW_SELECTED'));
        return;
    }

    if (!(await fw.dialog.confirm(fw.t('CNF.NOTI_DELETE', ids.length)))) {
        return;
    }

    const result = await fw.ajax(urls.delete, { method: 'POST', body: { notiIds: ids } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.DELETED'));
    refreshBadge();
    await load(page.pageNo);
}

body.addEventListener('click', (e) => {
    const button = e.target.closest('button[data-action="open"]');
    if (button) {
        open(button.closest('tr').dataset.notiId);
    }
});

checkAll.addEventListener('change', () => {
    for (const box of body.querySelectorAll('input[type="checkbox"]')) {
        box.checked = checkAll.checked;
    }
});

$('#noti-unread').addEventListener('change', () => load(1));
$('#noti-prev').addEventListener('click', () => load(page.pageNo - 1));
$('#noti-next').addEventListener('click', () => load(page.pageNo + 1));
$('#noti-read-all').addEventListener('click', async () => {
    if (await markRead(null)) {
        fw.notify(fw.t('MSG.NOTI_ALL_READ'));
        await load(1);
    }
});
deleteButton?.addEventListener('click', remove);

await load(1);
