// 포털 레이아웃 동작 (설계서 12장 '레이아웃'): 메뉴 트리(펼침·키보드·초성 검색·접기·드로어), 즐겨찾기·최근 사용,
// 멀티 탭(셸일 때), 세션 남은 시간. 메뉴 HTML은 서버가 권한으로 걸러서 그리므로 여기서는 보이기·숨기기만 한다.
import { fw } from './fw.js';

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
const userId = document.querySelector('meta[name="fw-user"]')?.content ?? '';
const store = {
    get(key, fallback) {
        try {
            return JSON.parse(localStorage.getItem(`portal.${key}.${userId}`)) ?? fallback;
        } catch {
            return fallback;
        }
    },
    set(key, value) {
        try {
            localStorage.setItem(`portal.${key}.${userId}`, JSON.stringify(value));
        } catch {
            // 저장소를 못 쓰는 브라우저 설정이면 기억하지 않는다
        }
    },
};

// ---------- 초성 검색 ----------
const CHO = 'ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ';

/** 한글 음절을 초성으로 바꾼다 ("공지사항" → "ㄱㅈㅅㅎ"). */
export function chosung(text) {
    return [...text].map((c) => {
        const code = c.charCodeAt(0) - 0xac00;
        return code >= 0 && code < 11172 ? CHO[Math.floor(code / 588)] : c;
    }).join('');
}

/** 메뉴명이 검색어와 맞는지 (대소문자 무시, 초성만 입력해도 맞음). */
export function matches(name, query) {
    const q = query.trim().toLowerCase();
    if (!q) {
        return true;
    }

    const n = name.toLowerCase();
    return n.includes(q) || chosung(n).includes(q);
}

// ---------- 메뉴 트리 ----------
const tree = $('.menu-tree');

function setExpanded(item, expanded) {
    item.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    const group = $(':scope > ul', item);
    if (group) {
        group.hidden = !expanded;
    }
}

function labelOf(item) {
    return $(':scope > .menu-label', item);
}

function visibleLabels() {
    return $$('.menu-label', tree).filter((label) => label.offsetParent !== null);
}

function focusLabel(label) {
    if (!label) {
        return;
    }

    $$('.menu-label[tabindex="0"]', tree).forEach((l) => l.setAttribute('tabindex', '-1'));
    label.setAttribute('tabindex', '0');
    label.focus();
}

function initTree() {
    if (!tree) {
        return;
    }

    const first = $('.menu-label', tree);
    first?.setAttribute('tabindex', '0');

    tree.addEventListener('click', (e) => {
        const label = e.target.closest('.menu-folder > .menu-label');
        if (label) {
            const item = label.parentElement;
            setExpanded(item, item.getAttribute('aria-expanded') !== 'true');
            focusLabel(label);
        }
    });

    // 키보드 탐색 (WAI-ARIA tree 패턴): ↑↓ 이동, → 펼침/첫 하위, ← 접힘/상위, Home/End, Enter 열기
    tree.addEventListener('keydown', (e) => {
        const label = e.target.closest('.menu-label');
        if (!label) {
            return;
        }

        const item = label.parentElement;
        const labels = visibleLabels();
        const index = labels.indexOf(label);
        const isFolder = item.classList.contains('menu-folder');
        const expanded = item.getAttribute('aria-expanded') === 'true';
        switch (e.key) {
            case 'ArrowDown': focusLabel(labels[index + 1]); break;
            case 'ArrowUp': focusLabel(labels[index - 1]); break;
            case 'Home': focusLabel(labels[0]); break;
            case 'End': focusLabel(labels[labels.length - 1]); break;
            case 'ArrowRight':
                if (isFolder && !expanded) {
                    setExpanded(item, true);
                } else if (isFolder) {
                    focusLabel(labelOf($(':scope > ul > li', item)));
                }
                break;
            case 'ArrowLeft':
                if (isFolder && expanded) {
                    setExpanded(item, false);
                } else {
                    focusLabel(labelOf(item.parentElement.closest('li[role="treeitem"]')));
                }
                break;
            case 'Enter':
            case ' ':
                if (isFolder) {
                    setExpanded(item, !expanded);
                } else {
                    label.click();
                }
                break;
            default:
                return;
        }

        e.preventDefault();
    });
}

/** 현재 화면 메뉴의 상위 폴더를 펼치고 표시한다. */
function revealMenu(menuId) {
    $$('.menu-label.current', tree).forEach((l) => l.classList.remove('current'));
    const link = tree && $(`a.menu-label[data-menu-id="${menuId}"]`, tree);
    if (!link) {
        return;
    }

    link.classList.add('current');
    for (let item = link.parentElement.parentElement.closest('li'); item; item = item.parentElement.closest('li')) {
        setExpanded(item, true);
    }
}

function findMenuByUrl(url) {
    const path = new URL(url, window.location.origin).pathname.toLowerCase();
    return $$('a.menu-label[data-menu-id]', tree ?? document)
        .find((a) => new URL(a.href).pathname.toLowerCase() === path)?.dataset.menuId;
}

// ---------- 메뉴 검색 ----------
function initSearch() {
    const input = $('#menu-search');
    if (!input || !tree) {
        return;
    }

    const empty = $('.menu-empty');
    const filter = () => {
        const query = input.value;
        showSideTab('all');
        const visit = (item) => {
            const own = matches(labelOf(item)?.textContent ?? '', query);
            const kids = $$(':scope > ul > li', item).map(visit).some(Boolean);
            const show = own || kids;
            item.hidden = !show;
            if (query && item.classList.contains('menu-folder')) {
                setExpanded(item, kids || own);
            }

            return show;
        };
        const any = $$(':scope > li', tree).map(visit).some(Boolean);
        if (empty) {
            empty.hidden = any;
        }
    };
    input.addEventListener('input', filter);
    input.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            input.value = '';
            filter();
        } else if (e.key === 'Enter') {
            const hit = $$('a.menu-label', tree).find((a) => a.offsetParent !== null);
            hit?.click();
            e.preventDefault();
        } else if (e.key === 'ArrowDown') {
            focusLabel(visibleLabels()[0]);
            e.preventDefault();
        }
    });
}

// ---------- 좌측 탭 (전체/즐겨찾기/최근) ----------
function showSideTab(name) {
    $$('.side-tabs [role="tab"]').forEach((tab) => {
        const selected = tab.id === `side-tab-${name}`;
        tab.setAttribute('aria-selected', selected ? 'true' : 'false');
        tab.setAttribute('tabindex', selected ? '0' : '-1');
        const panel = document.getElementById(tab.getAttribute('aria-controls'));
        if (panel) {
            panel.hidden = !selected;
        }
    });
}

function initSideTabs() {
    const tabs = $$('.side-tabs [role="tab"]');
    tabs.forEach((tab, i) => {
        tab.addEventListener('click', () => showSideTab(tab.id.replace('side-tab-', '')));
        tab.addEventListener('keydown', (e) => {
            const next = e.key === 'ArrowRight' ? tabs[(i + 1) % tabs.length] : e.key === 'ArrowLeft' ? tabs[(i - 1 + tabs.length) % tabs.length] : null;
            if (next) {
                next.focus();
                next.click();
            }
        });
    });
}

// ---------- 즐겨찾기·최근 사용 ----------
function listItemFor(menuId) {
    const source = tree && $(`a.menu-label[data-menu-id="${menuId}"]`, tree);
    if (!source) {
        return null;
    }

    const li = document.createElement('li');
    li.dataset.menuId = menuId;
    const a = source.cloneNode(true);
    a.removeAttribute('tabindex');
    a.classList.remove('current');
    li.append(a);
    return li;
}

function refreshEmpty(list) {
    $(':scope > .list-empty', list)?.remove();
    if (list.dataset.emptyText && !$(':scope > li[data-menu-id]', list)) {
        const li = document.createElement('li');
        li.className = 'list-empty hint';
        li.textContent = list.dataset.emptyText;
        list.append(li);
    }
}

function initFavorites() {
    const list = $('#favorite-list');
    if (!tree || !list) {
        return;
    }

    refreshEmpty(list);
    tree.addEventListener('click', async (e) => {
        const button = e.target.closest('button.fav');
        if (!button) {
            return;
        }

        const menuId = Number(button.dataset.menuId);
        const favorite = button.getAttribute('aria-pressed') !== 'true';
        const result = await fw.ajax('/My/Favorite', { method: 'POST', body: { menuId, favorite } });
        if (!result.success) {
            fw.notify(result.error?.message ?? fw.t('MSG.SYSTEM_ERROR'));
            return;
        }

        button.setAttribute('aria-pressed', favorite ? 'true' : 'false');
        button.setAttribute('aria-label', favorite ? button.dataset.labelRemove : button.dataset.labelAdd);
        $(`:scope > li[data-menu-id="${menuId}"]`, list)?.remove();
        if (favorite) {
            const item = listItemFor(menuId);
            if (item) {
                list.append(item);
            }
        }

        refreshEmpty(list);
    });
}

const MAX_RECENT = 10;

function renderRecent() {
    const list = $('#recent-list');
    if (!list) {
        return;
    }

    list.replaceChildren(...store.get('recent', []).map(listItemFor).filter(Boolean));
}

function rememberRecent(menuId) {
    if (!menuId) {
        return;
    }

    const ids = [String(menuId), ...store.get('recent', []).filter((id) => id !== String(menuId))].slice(0, MAX_RECENT);
    store.set('recent', ids);
    renderRecent();
}

// ---------- 멀티 탭 ----------
const TAB_PARAM = '_layout';

function tabUrl(url) {
    const u = new URL(url, window.location.origin);
    u.searchParams.set(TAB_PARAM, 'tab');
    return u.pathname + u.search + u.hash;
}

function tabKey(url) {
    const u = new URL(url, window.location.origin);
    u.searchParams.delete(TAB_PARAM);
    return u.pathname.toLowerCase() + u.search;
}

class Tabs {
    constructor(root) {
        this.root = root;
        this.list = $('.tab-list', root);
        this.panels = $('.tab-panels', root);
        this.maxTabs = Number(root.dataset.maxTabs) || 10;
        this.tabs = new Map();
        this.seq = 0;
        $$('[data-tab-action]', root).forEach((b) => b.addEventListener('click', () => {
            const active = this.active();
            if (!active) {
                return;
            }

            if (b.dataset.tabAction === 'refresh') {
                active.frame.contentWindow?.location.reload();
            } else {
                [...this.tabs.keys()].filter((k) => k !== active.key).forEach((k) => this.close(k));
            }
        }));
        this.list.addEventListener('keydown', (e) => {
            const keys = [...this.tabs.keys()];
            const i = keys.indexOf(this.active()?.key);
            const next = e.key === 'ArrowRight' ? keys[(i + 1) % keys.length] : e.key === 'ArrowLeft' ? keys[(i - 1 + keys.length) % keys.length] : null;
            if (next) {
                this.activate(next);
                this.tabs.get(next).button.focus();
                e.preventDefault();
            } else if (e.key === 'Delete' && this.active()) {
                this.close(this.active().key);
            }
        });
    }

    active() {
        return [...this.tabs.values()].find((t) => t.button.getAttribute('aria-selected') === 'true');
    }

    open(url, title, menuId) {
        const key = tabKey(url);
        if (this.tabs.has(key)) {
            this.activate(key);
            return;
        }

        if (this.tabs.size >= this.maxTabs) {
            const oldest = [...this.tabs.values()].sort((a, b) => a.used - b.used)[0];
            this.close(oldest.key);
            fw.notify(fw.t('MSG.TAB_LIMIT', this.maxTabs));
        }

        const id = `tab-${++this.seq}`;
        const wrap = document.createElement('div');
        wrap.className = 'tab';
        const button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('role', 'tab');
        button.id = `${id}-btn`;
        button.setAttribute('aria-controls', id);
        button.textContent = title || '…';
        button.addEventListener('click', () => this.activate(key));
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'tab-close';
        close.tabIndex = -1;
        close.setAttribute('aria-label', `${fw.t('BTN.CLOSE_TAB')} ${button.textContent}`);
        close.append(fw.icon('x'));
        close.addEventListener('click', () => this.close(key));
        wrap.append(button, close);
        this.list.append(wrap);

        const panel = document.createElement('div');
        panel.id = id;
        panel.className = 'tab-panel';
        panel.setAttribute('role', 'tabpanel');
        panel.setAttribute('aria-labelledby', button.id);
        const frame = document.createElement('iframe');
        frame.title = button.textContent;
        frame.src = tabUrl(url);
        frame.addEventListener('load', () => {
            // 같은 출처이므로 화면 제목을 탭 이름으로 쓴다 (화면 안에서 링크로 이동해도 따라간다)
            const doc = frame.contentDocument;
            const name = doc?.title || title;
            if (name) {
                button.textContent = name;
                frame.title = name;
                close.setAttribute('aria-label', `${fw.t('BTN.CLOSE_TAB')} ${name}`);
            }

            if (this.active()?.key === key) {
                this.syncAddress(key);
            }

            shell.touch();
        });
        panel.append(frame);
        this.panels.append(panel);

        this.tabs.set(key, { key, url, menuId, button, wrap, panel, frame, used: 0 });
        this.activate(key);
    }

    activate(key) {
        const tab = this.tabs.get(key);
        if (!tab) {
            return;
        }

        for (const t of this.tabs.values()) {
            const selected = t === tab;
            t.button.setAttribute('aria-selected', selected ? 'true' : 'false');
            t.button.tabIndex = selected ? 0 : -1;
            t.panel.hidden = !selected;
        }

        tab.used = performance.now();
        if (tab.menuId) {
            revealMenu(tab.menuId);
        }

        this.syncAddress(key);
    }

    // 주소창을 보이는 탭 화면 주소로 맞춘다 (새로고침·주소 복사 시 같은 화면이 열리도록)
    syncAddress(key) {
        const tab = this.tabs.get(key);
        let path = tab.url;
        try {
            const loc = tab.frame.contentWindow?.location;
            if (loc && loc.href !== 'about:blank') {
                const u = new URL(loc.href);
                u.searchParams.delete(TAB_PARAM);
                path = u.pathname + u.search;
            }
        } catch {
            // 다른 출처로 이동한 탭(없어야 정상)
        }

        history.replaceState(null, '', path);
    }

    close(key) {
        const tab = this.tabs.get(key);
        if (!tab) {
            return;
        }

        const wasActive = this.active() === tab;
        tab.wrap.remove();
        tab.panel.remove();
        this.tabs.delete(key);
        if (wasActive) {
            const next = [...this.tabs.values()].sort((a, b) => b.used - a.used)[0];
            if (next) {
                this.activate(next.key);
            } else {
                this.open('/', fw.t('SITE.NAME'), findMenuByUrl('/'));
            }
        }
    }
}

let tabs = null;

function openMenuLink(link) {
    const menuId = link.dataset.menuId;
    rememberRecent(menuId);
    if (tabs) {
        tabs.open(link.getAttribute('href'), link.textContent.trim(), menuId);
    } else {
        window.location.assign(link.href);
    }

    // 좁은 화면에서는 메뉴를 고른 뒤 드로어를 닫는다
    if (window.matchMedia('(max-width: 768px)').matches) {
        setSideOpen(false);
    }
}

function initLinks() {
    // 메뉴·즐겨찾기·최근·사용자 메뉴의 화면 링크 (data-tab="Y"). 새 창 링크는 브라우저 기본 동작.
    document.addEventListener('click', (e) => {
        const link = e.target.closest('a[data-tab="Y"]');
        if (!link || e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) {
            return;
        }

        e.preventDefault();
        link.closest('details')?.removeAttribute('open');
        openMenuLink(link);
    });
}

// ---------- 메뉴 접기·드로어 ----------
function setSideOpen(open) {
    const toggle = $('.menu-toggle');
    document.body.classList.toggle('side-collapsed', !open);
    toggle?.setAttribute('aria-expanded', open ? 'true' : 'false');
}

function initSideToggle() {
    const narrow = window.matchMedia('(max-width: 768px)');
    setSideOpen(narrow.matches ? false : !store.get('sideCollapsed', false));
    $('.menu-toggle')?.addEventListener('click', () => {
        const open = document.body.classList.contains('side-collapsed');
        setSideOpen(open);
        if (!narrow.matches) {
            store.set('sideCollapsed', !open);
        }
    });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && narrow.matches && !document.body.classList.contains('side-collapsed')) {
            setSideOpen(false);
            $('.menu-toggle')?.focus();
        }
    });
}

// ---------- 세션 남은 시간 ----------
const shell = {
    lastActivity: Date.now(),
    touch() {
        this.lastActivity = Date.now();
    },
};
window.fwShell = shell;

function initSession() {
    const box = $('.session');
    const out = $('#session-remain');
    const extend = $('#session-extend');
    const idle = Number(box?.dataset.idleMinutes) * 60 * 1000;
    if (!box || !out || !idle) {
        return;
    }

    let warned = false;
    const tick = () => {
        const left = idle - (Date.now() - shell.lastActivity);
        if (left <= 0) {
            window.location.assign('/Account/Login?reason=AUTH.SESSION_EXPIRED');
            return;
        }

        const seconds = Math.floor(left / 1000);
        out.textContent = `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
        const ending = seconds <= 120;
        box.classList.toggle('ending', ending);
        if (ending && !warned) {
            warned = true;
            fw.notify(fw.t('MSG.SESSION_ENDING'));
        } else if (!ending) {
            warned = false;
        }
    };
    extend?.addEventListener('click', async () => {
        const result = await fw.ajax('/My/Ping');
        if (result.success) {
            shell.touch();
            tick();
        }
    });
    tick();
    setInterval(tick, 1000);
}

// ---------- 탭으로 화면 열기 (탭 안 화면에서 window.top.fwShell.openUrl) ----------
function openUrl(url, title) {
    const menuId = findMenuByUrl(url);
    const label = menuId ? $(`a.menu-label[data-menu-id="${menuId}"]`, tree)?.textContent.trim() : '';
    rememberRecent(menuId);
    if (tabs) {
        tabs.open(url, label || title, menuId);
    } else {
        window.location.assign(url);
    }
}

shell.openUrl = openUrl;

// ---------- 알림 배지 ----------
// 안 읽은 알림 수를 주기마다 센다. 사용자가 한 요청이 아니므로 세션을 연장하지 않고(X-Portal-Background),
// 세션 타이머도 건드리지 않는다(fw.ajax를 쓰지 않는다). 탭이 숨겨져 있으면 세지 않는다.
function initNotifications() {
    const bell = $('#noti-bell');
    if (!bell) {
        return;
    }

    const badge = $('.noti-badge', bell);
    const seconds = Math.max(15, Number(bell.dataset.pollSeconds) || 60);
    let timer = null;
    const render = (count) => {
        badge.hidden = count === 0;
        badge.textContent = count > 99 ? '99+' : String(count);
        const label = count > 0 ? fw.t('LBL.NOTI_UNREAD_CNT', count > 99 ? '99+' : count) : fw.t('LBL.NOTIFICATIONS');
        bell.setAttribute('aria-label', label);
        bell.title = label;
    };
    const refresh = async () => {
        if (document.hidden) {
            return;
        }

        try {
            const response = await fetch(bell.dataset.urlUnread, {
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json', 'X-Portal-Background': '1' },
            });
            if (response.status === 401) {
                // 세션이 끝났다: 세션 타이머가 로그인 화면으로 보낸다
                clearInterval(timer);
                return;
            }

            const result = response.ok ? await response.json() : null;
            if (result?.success) {
                render(result.data.count);
            }
        } catch {
            // 네트워크 오류는 다음 주기에 다시 센다
        }
    };

    shell.refreshNotifications = refresh;
    bell.addEventListener('click', (e) => {
        if (e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) {
            return;
        }

        e.preventDefault();
        openUrl(bell.getAttribute('href'), fw.t('LBL.NOTIFICATIONS'));
    });
    document.addEventListener('visibilitychange', refresh);
    refresh();
    timer = setInterval(refresh, seconds * 1000);
}

// ---------- 시작 ----------
await fw.ready;
initTree();
initSearch();
initSideTabs();
initFavorites();
renderRecent();
initLinks();
initSideToggle();
initSession();
initNotifications();

const tabRoot = $('#fw-tabs');
if (tabRoot) {
    tabs = new Tabs(tabRoot);
    const initial = tabRoot.dataset.initialUrl || '/';
    const menuId = findMenuByUrl(initial);
    tabs.open(initial, menuId ? $(`a.menu-label[data-menu-id="${menuId}"]`, tree)?.textContent.trim() : '', menuId);
} else {
    const menuId = findMenuByUrl(window.location.href);
    if (menuId) {
        revealMenu(menuId);
    }
}
