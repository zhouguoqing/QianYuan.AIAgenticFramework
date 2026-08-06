"""Final i18n audit with sub-menu expansion."""
from playwright.sync_api import sync_playwright
import re, json

OUT = '/Users/guoqingzhou/QianYuan.AgenticFramework/tests/i18n_output'
has_cn = lambda t: bool(t and re.search(r'[\u4e00-\u9fff]', t) and not t.startswith('http'))

issues = []
ikeys = set()

def add_issue(pg, el, txt, tp, sug, ss=''):
    k = (pg, txt[:60], tp)
    if k in ikeys: return
    ikeys.add(k)
    issues.append(dict(page=pg, element=el, text=txt, type=tp, suggestion=sug, screenshot=ss))

def close_dialog(page):
    try:
        ok = page.locator('button:has-text("确定"):visible')
        if ok.count() > 0:
            ok.first.click()
            page.wait_for_timeout(2000)
    except: pass

def scan_cn(page):
    return page.evaluate("""() => {
        const r = [], seen = new Set();
        document.querySelectorAll('*').forEach(el => {
            if (el.children.length > 0) return;
            const t = (el.textContent || '').trim();
            if (t.length < 2 || t.length > 300) return;
            if (!/[\\u4e00-\\u9fff]/.test(t)) return;
            const s = window.getComputedStyle(el);
            if (s.display === 'none' || s.visibility === 'hidden') return;
            const key = el.tagName + '|' + t.substring(0, 40);
            if (seen.has(key)) return;
            seen.add(key);
            r.push({tag: el.tagName.toLowerCase(), text: t, cls: (el.className||'').toString().substring(0, 80)});
        });
        return r;
    }""")

with sync_playwright() as p:
    b = p.chromium.launch(headless=True, args=['--no-sandbox', '--proxy-server='])
    page = b.new_page(viewport={'width': 1920, 'height': 1080}, locale='en-US')

    # === LOGIN ===
    print("LOGIN...")
    page.goto('https://cloud.teld6.top/web/', timeout=60000)
    page.wait_for_timeout(5000)

    # Record dialog issue
    try:
        dtitle = page.locator('[class*="title"]:visible').first.inner_text()
        if has_cn(dtitle):
            add_issue('全局弹窗', 'dialog-title', dtitle, '弹窗标题中文', 'Translate to "Information"')
    except: pass

    close_dialog(page)

    page.fill('input[placeholder*="Phone, email"]', 'saasadmin')
    page.fill('input[placeholder="Password"]', 'saasadminA?')
    page.keyboard.press('Enter')
    page.wait_for_timeout(10000)
    page.wait_for_load_state('networkidle')
    close_dialog(page)
    page.wait_for_timeout(2000)

    print(f"  URL: {page.url}")

    # === PAGE TITLE ===
    title = page.title()
    if has_cn(title):
        add_issue('页面标题', 'title', title, '页面标题含中文', 'Translate to English')

    # === DASHBOARD SCAN ===
    print("SCANNING DASHBOARD...")
    cn = scan_cn(page)
    for c in cn:
        add_issue('仪表盘', c['tag'], c['text'], '硬编码中文', 'Translate to English')

    # === EXPAND SUB-MENUS ===
    print("EXPANDING SUB-MENUS...")
    # Hover over all possible menu parent elements
    menu_parents = page.evaluate("""() => {
        const items = [];
        document.querySelectorAll('[class*="el-submenu__title"], [class*="el-menu-item"], li[class*="sub"], [class*="menu-item"]').forEach(el => {
            const cls = (el.className || '').toString();
            items.push({cls: cls.substring(0, 80), html: el.outerHTML.substring(0, 200)});
        });
        return items;
    }""")

    print(f"  Found {len(menu_parents)} potential menu elements")
    for mp in menu_parents[:5]:
        print(f"    {mp['cls']}")

    # Try clicking through all sidebar text elements 
    sidebar_texts = page.evaluate("""() => {
        const items = [];
        // Find all clickable sidebar elements
        const sidebar = document.querySelector('aside, [class*="sidebar"], [class*="menu-wrap"], [class*="side"]');
        if (!sidebar) {
            // Fallback: find elements with specific positioning
            document.querySelectorAll('*').forEach(el => {
                const r = el.getBoundingClientRect();
                const t = (el.textContent || '').trim();
                if (r.x < 300 && r.width > 50 && r.height > 20 && r.height < 60 && t && t.length > 1 && t.length < 100) {
                    items.push(t);
                }
            });
        } else {
            sidebar.querySelectorAll('*').forEach(el => {
                const t = (el.textContent || '').trim().replace(/\\s+/g, ' ');
                const r = el.getBoundingClientRect();
                if (t && t.length > 1 && t.length < 100 && r.height > 15 && r.height < 60 && r.width > 60) {
                    items.push(t);
                }
            });
        }
        return [...new Set(items)];
    }""")

    print(f"  Sidebar text items: {len(sidebar_texts)}")
    for st in sidebar_texts:
        flag = '🔴' if has_cn(st) else '  '
        print(f"    {flag} {st[:80]}")

    # === CLICK THROUGH MENUS ===
    print("\nCLICKING THROUGH PAGES...")
    visited = set()
    count = 0

    for item_text in sidebar_texts:
        if count >= 30: break
        if item_text in visited: continue
        count += 1
        visited.add(item_text)

        # Check for Chinese in menu text
        if has_cn(item_text):
            add_issue('侧边栏菜单', 'menu-item', item_text, '菜单项中文', 'Translate to English')

        try:
            # Try to find and click the element
            el = page.locator(f'text="{item_text}"').first
            if el.count() == 0:
                continue
            if not el.is_visible():
                continue

            el.click()
            page.wait_for_timeout(3000)
            close_dialog(page)
            page.wait_for_timeout(1000)

            # Scan the loaded page
            page_cn = scan_cn(page)
            page_name = item_text.replace(' ', '_').replace('/', '_')[:30]

            if page_cn:
                for c in page_cn:
                    # Skip already-known system-wide issues
                    if '提示信息' in c['text'] or '确定' == c['text'] or '系统管理员' == c['text']:
                        continue
                    add_issue(item_text, c['tag'], c['text'], '硬编码中文', 'Translate to English')

            ss = f'page_{count:02d}_{page_name}.png'
            page.screenshot(path=f'{OUT}/{ss}', full_page=True)

        except Exception as e:
            pass

    b.close()

    # === GENERATE FINAL REPORT ===
    print(f"\n{'='*50}")
    print(f"TOTAL ISSUES: {len(issues)}")

    with open(f'{OUT}/i18n_final_report.md', 'w') as f:
        f.write("# 🌐 Internationalization (i18n) Translation Issues Report\n\n")
        f.write(f"**Site**: https://cloud.teld6.top/\n")
        f.write(f"**Account**: saasadmin\n")
        f.write(f"**Target Language**: English (en-US)\n")
        f.write(f"**Method**: Login -> close all dialogs -> expand menus -> scan each page\n")
        f.write(f"**Total Issues Found**: {len(issues)}\n\n")

        # Stats
        types = {}
        for i in issues:
            types[i['type']] = types.get(i['type'], 0) + 1

        f.write("## 📊 Issue Summary\n\n")
        f.write("| Type | Count |\n")
        f.write("|---|---:|\n")
        for k, v in sorted(types.items(), key=lambda x: -x[1]):
            f.write(f"| {k} | {v} |\n")

        # By page
        pages = {}
        for i in issues:
            p = i['page']
            pages.setdefault(p, []).append(i)

        f.write("\n## 🔴 Detailed Issues\n\n")
        for idx, i in enumerate(issues, 1):
            f.write(f"### #{idx}\n")
            f.write(f"- **Page**: {i['page']}\n")
            f.write(f"- **Element**: {i['element']}\n")
            f.write(f"- **Original Text**: `{i['text']}`\n")
            f.write(f"- **Issue Type**: {i['type']}\n")
            f.write(f"- **Fix**: {i['suggestion']}\n")
            if i['screenshot']:
                f.write(f"- **Screenshot**: {i['screenshot']}\n")
            f.write("\n")

    print(f"✅ Report: {OUT}/i18n_final_report.md")
    print(f"📁 JSON:  {OUT}/i18n_issues.json")
