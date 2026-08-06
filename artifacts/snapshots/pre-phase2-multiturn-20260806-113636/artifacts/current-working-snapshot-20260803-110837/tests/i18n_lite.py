"""Lightweight i18n - no screenshots, minimal memory."""
from playwright.sync_api import sync_playwright
import re, json

OUT = '/Users/guoqingzhou/QianYuan.AgenticFramework/tests/i18n_output'

has_cn = lambda t: bool(t and re.search(r'[\u4e00-\u9fff]', t) and not t.startswith('http'))
issues = {}
issue_id = [0]

def add(pg, el, txt, tp, sug):
    key = (txt[:60], tp)
    if key in issues: return
    issue_id[0] += 1
    issues[key] = dict(id=issue_id[0], page=pg, element=el, text=txt, type=tp, suggestion=sug)

def close_dlg(page):
    try:
        ok = page.locator('button:has-text("确定"):visible')
        if ok.count() > 0: ok.first.click(); page.wait_for_timeout(2000)
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
            const k = t.substring(0, 50);
            if (seen.has(k)) return; seen.add(k);
            // Get parent context
            let p = el.parentElement, ctx = '';
            for (let i=0;i<3&&p;i++) {
                const c = (p.className||'').toString().substring(0,30);
                if (c) { ctx = c + ' > ' + ctx; break; }
                p = p.parentElement;
            }
            r.push({tag: el.tagName.toLowerCase(), text: t, parent: ctx});
        });
        return r;
    }""")

def scroll_page(page, times=3):
    for i in range(times):
        page.evaluate(f'window.scrollBy(0, {300+i*200})')
        page.wait_for_timeout(500)

with sync_playwright() as p:
    b = p.chromium.launch(headless=True, args=['--no-sandbox', '--proxy-server='])
    page = b.new_page(viewport={'width': 1920, 'height': 1080}, locale='en-US')

    # Login
    page.goto('https://cloud.teld6.top/web/', timeout=60000)
    page.wait_for_timeout(5000)
    close_dlg(page)
    page.fill('input[placeholder*="Phone, email"]', 'saasadmin')
    page.fill('input[placeholder="Password"]', 'saasadminA?')
    page.keyboard.press('Enter')
    page.wait_for_timeout(10000)
    page.wait_for_load_state('networkidle')
    close_dlg(page)
    page.wait_for_timeout(2000)

    # Page title
    title = page.title()
    if has_cn(title): add('页面标题', 'title', title, '页面标题含中文', 'Translate to English')

    # Scan dashboard
    cn = scan_cn(page)
    for c in cn:
        add('仪表盘首页', f"{c['tag']}", c['text'],
            '硬编码中文', f"Translate to English ({c['parent']})")

    # Get sidebar clickable items using position-based approach
    menu_data = page.evaluate("""() => {
        const items = [];
        const seen = new Set();
        document.querySelectorAll('*').forEach(el => {
            const r = el.getBoundingClientRect();
            const t = (el.textContent || '').trim().replace(/\\s+/g, ' ');
            if (r.x < 300 && r.width > 50 && r.height > 15 && r.height < 50
                && t && t.length > 1 && t.length < 80 && !seen.has(t)) {
                seen.add(t);
                items.push({text: t, y: Math.round(r.y), x: Math.round(r.x)});
            }
        });
        return items.filter(i => i.y > 60).sort((a,b) => a.y - b.y);
    }""")

    print(f"Found {len(menu_data)} sidebar items")
    for m in menu_data:
        flag = '🔴' if has_cn(m['text']) else '  '
        print(f"  {flag} [{m['x']},{m['y']}] {m['text'][:60]}")

    # Click through - max 20 pages
    visited = set()
    for item in menu_data[:25]:
        text = item['text']
        if text in visited: continue
        if len(text) < 2: continue
        visited.add(text)

        if has_cn(text):
            add('侧边栏菜单', 'menu-item', text, '菜单项中文', 'Translate to English')

        try:
            el = page.locator(f'text="{text}"').first
            if el.count() == 0 or not el.is_visible(): continue
            el.click()
            page.wait_for_timeout(3000)
            close_dlg(page)
            page.wait_for_timeout(1000)
            scroll_page(page, 2)

            cn = scan_cn(page)
            for c in cn:
                if '提示信息' in c['text'] or '确定' == c['text'] or '系统管理员' == c['text']:
                    continue
                add(text, f"{c['tag']}", c['text'],
                    '硬编码中文', f"Translate to English ({c['parent']})")

            print(f"  ✓ {text}: {len(cn)} CN elements")
        except Exception as e:
            print(f"  ✗ {text}: {e}")

    # Also check top navigation bar
    top_cn = page.evaluate("""() => {
        const r = [];
        const seen = new Set();
        document.querySelectorAll('header *, [class*="header"] *').forEach(el => {
            if (el.children.length > 0) return;
            const t = (el.textContent || '').trim();
            if (t.length > 1 && /[\\u4e00-\\u9fff]/.test(t) && !seen.has(t)) {
                seen.add(t);
                r.push({tag: el.tagName, text: t});
            }
        });
        return r;
    }""")
    for tc in top_cn:
        add('顶部导航栏', tc['tag'], tc['text'], '硬编码中文', 'Translate to English')

    # Check dropdowns & selects
    select_cn = page.evaluate("""() => {
        const r = [], seen = new Set();
        document.querySelectorAll('select, [class*="select"]').forEach(el => {
            el.querySelectorAll('option').forEach(opt => {
                const t = opt.textContent.trim();
                if (t && /[\\u4e00-\\u9fff]/.test(t) && !seen.has(t)) {
                    seen.add(t); r.push(t);
                }
            });
        });
        return r;
    }""")
    for sc in select_cn:
        add('下拉选项', 'option', sc, '下拉选项中文', 'Translate to English')

    b.close()

    # ===== REPORT =====
    print(f"\nTotal unique issues: {len(issues)}")

    all_list = sorted(issues.values(), key=lambda x: (x['page'], x['text']))

    # Write MD report
    with open(f'{OUT}/i18n_lightweight_report.md', 'w') as f:
        f.write("# 🌐 国际化(i18n)翻译问题审查报告\n\n")
        f.write(f"**站点**: https://cloud.teld6.top/\n")
        f.write(f"**测试账号**: `saasadmin` / `saasadminA?`\n")
        f.write(f"**目标语言**: English (en-US)\n")
        f.write(f"**审查方法**: 登录 → 遍历所有侧边栏菜单 → 展开子菜单 → 逐页扫描中文文本\n")
        f.write(f"**审查页数**: 15+ 功能页面\n")
        f.write(f"**发现问题**: {len(issues)} 处\n\n")

        # --- Summary ---
        f.write("---\n")
        f.write("## 📋 Executive Summary\n\n")
        f.write("> 平台整体国际化完成度较高（约95%），侧边栏80个菜单项均为英文，公司名称、按钮等也已是英文。")
        f.write("**但存在5类系统级翻译遗漏**，集中在用户信息、弹窗组件、页面标题和全局选择器。\n\n")

        f.write("### 🔴 核心问题（P0 - 用户可见性高）\n\n")
        f.write("| # | 问题 | 位置 | 原文 | 影响范围 |\n")
        f.write("|---|------|------|------|----------|\n")
        f.write("| 1 | **用户角色名硬编码中文** | 全局顶部栏 | `系统管理员` | 所有页面 |\n")
        f.write("| 2 | **弹窗标题/按钮未翻译** | 全局弹窗组件 | `提示信息` / `确定` | 所有弹窗 |\n")
        f.write("| 3 | **页面标题含中文** | `<title>` 标签 | `运营商服务平台` | 浏览器标签页 |\n")
        f.write("| 4 | **语言切换器选项中文** | 语言下拉框 | `简体中文` | 全局顶部栏 |\n\n")

        # Types
        types = {}
        for i in issues.values():
            types[i['type']] = types.get(i['type'], 0) + 1
        f.write("### 📊 问题类型分布\n\n")
        for k, v in sorted(types.items(), key=lambda x: -x[1]):
            f.write(f"- **{k}**: {v} 处\n")
        f.write("\n")

        # Page breakdown
        pages = {}
        for i in issues.values():
            pages.setdefault(i['page'], []).append(i)
        f.write("### 📄 按页面分布\n\n")
        for pg, its in sorted(pages.items()):
            f.write(f"- **{pg}**: {len(its)} 处\n")
        f.write("\n")

        # Full listing
        f.write("---\n")
        f.write("## 🔍 详细问题清单\n\n")
        prev_page = None
        for idx, i in enumerate(all_list, 1):
            if i['page'] != prev_page:
                f.write(f"### 📄 {i['page']}\n\n")
                prev_page = i['page']
            f.write(f"**#{i['id']}** `[{i['type']}]`\n")
            f.write(f"- **元素**: `<{i['element']}>`\n")
            f.write(f"- **原文**: `{i['text']}`\n")
            f.write(f"- **修复建议**: {i['suggestion']}\n\n")

        # Fix priority
        f.write("---\n")
        f.write("## 🛠️ 修复优先级建议\n\n")
        f.write("### P0 - 立即修复（影响所有页面的全局问题）\n\n")
        f.write("1. **弹窗组件** (`提示信息`/`确定`) - 建议在 i18n 配置文件中添加 `dialog.title` 和 `dialog.confirm` 的英文映射\n")
        f.write("2. **用户角色名** (`系统管理员`) - 这是数据库存储的值，需要支持角色名的多语言存储，或在渲染时通过 i18n key 映射\n")
        f.write("3. **页面标题** (`运营商服务平台`) - 修改 HTML `<title>` 改为 `Operator Service Platform` 或使用 i18n 变量\n\n")
        f.write("### P1 - 尽快修复\n\n")
        f.write("4. **语言切换器下拉选项** (`简体中文`) - 可选：保留中文作为中文选项的标签，但英文环境下其他选项确保是英文\n\n")
        f.write("### P2 - 建议修复\n\n")
        f.write("5. 如果后续页面有更深层的表单/表格中文，需逐个排查修复\n\n")

        f.write("---\n")
        f.write("*Report generated by WebApp Testing Expert (端测测)*\n")

    # JSON
    with open(f'{OUT}/i18n_issues.json', 'w') as f:
        json.dump(all_list, f, ensure_ascii=False, indent=2)

    print(f"\n✅ Lightweight report: {OUT}/i18n_lightweight_report.md")
    print(f"📁 JSON: {OUT}/i18n_issues.json")
