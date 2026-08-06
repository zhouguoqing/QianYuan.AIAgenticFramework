"""Deep i18n audit - close dialogs and explore sidebar menus and all pages."""
from playwright.sync_api import sync_playwright
import re, json

OUT = '/Users/guoqingzhou/QianYuan.AgenticFramework/tests/i18n_output'
has_cn = lambda t: bool(t and re.search(r'[\u4e00-\u9fff]', t) and not t.startswith('http'))

all_issues = []
issue_keys = set()

def log_issue(page_name, element, text, issue_type, suggestion, screenshot=''):
    key = (page_name, text[:80], issue_type)
    if key in issue_keys: return
    issue_keys.add(key)
    all_issues.append(dict(page=page_name, element=element, text=text, type=issue_type, suggestion=suggestion, screenshot=screenshot))
    ic = '🔴' if '混杂' in issue_type else '🟡'
    print(f'  {ic} [{issue_type}] {page_name} / {element}: {text[:100]}')

def close_dialog(page):
    try:
        ok = page.locator('button:has-text("确定"):visible')
        if ok.count() > 0 and ok.first.is_visible():
            ok.first.click()
            page.wait_for_timeout(3000)
            return True
    except: pass
    return False

def scan_page(page, page_name):
    """Scan for ALL Chinese text on current page."""
    cn_items = page.evaluate('''() => {
        const r = [];
        const seen = new Set();
        document.querySelectorAll('*').forEach(el => {
            // Only leaf elements with text content
            if (el.children.length > 0) return;
            const t = (el.textContent || '').trim();
            if (t.length < 2 || t.length > 300) return;
            if (!/[\u4e00-\u9fff]/.test(t)) return;
            const s = window.getComputedStyle(el);
            if (s.display === 'none' || s.visibility === 'hidden') return;
            
            const key = el.tagName + '|' + t.substring(0, 50);
            if (seen.has(key)) return;
            seen.add(key);
            
            let parentContext = '';
            let p = el.parentElement;
            for (let i = 0; i < 3 && p; i++) {
                const pcls = (p.className||'').toString().substring(0, 40);
                const pid = p.id || '';
                if (pcls || pid) {
                    parentContext = (pid ? '#'+pid : '') + (pcls ? '.'+pcls : '') + ' > ' + parentContext;
                    break;
                }
                p = p.parentElement;
            }
            
            r.push({
                tag: el.tagName.toLowerCase(),
                text: t,
                cls: (el.className||'').toString().substring(0, 80),
                parentCtx: parentContext
            });
        });
        return r;
    }''')
    return cn_items

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--no-sandbox','--proxy-server='])
    page = browser.new_page(viewport={'width': 1920, 'height': 1080}, locale='en-US')
    
    # ===== LOGIN =====
    print("=" * 60)
    print("PHASE 1: LOGIN")
    page.goto('https://cloud.teld6.top/web/', timeout=60000)
    page.wait_for_timeout(5000)
    
    # Record initial dialog issue
    try:
        dtitle = page.locator('[class*="title"]:visible').first.inner_text()
        if has_cn(dtitle):
            log_issue("登录前弹窗", "dialog-title", dtitle, "弹窗标题中文", "Translate to 'Information'")
    except: pass
    close_dialog(page)
    
    page.fill('input[placeholder*="Phone, email"]', 'saasadmin')
    page.fill('input[placeholder="Password"]', 'saasadminA?')
    page.keyboard.press('Enter')
    page.wait_for_timeout(10000)
    page.wait_for_load_state('networkidle')
    
    render_url = page.url
    print(f"  Logged in: {render_url}")
    
    # Close dialog on dashboard
    close_dialog(page)
    page.wait_for_timeout(3000)
    page.screenshot(path=f'{OUT}/dashboard_base.png', full_page=True)
    
    # ===== SCAN DASHBOARD =====
    print("\n" + "=" * 60)
    print("PHASE 2: DASHBOARD SCAN")
    
    cn_items = scan_page(page, "dashboard")
    for ci in cn_items:
        log_issue("仪表盘首页", f"{ci['tag']}", ci['text'], 
                 "硬编码中文", f"Translate '{ci['text'][:30]}' to English", 'dashboard_base.png')
    
    # Also check page title
    title = page.title()
    if has_cn(title):
        log_issue("仪表盘首页", "page.title", title, "页面标题含中文", "Translate page title to English", 'dashboard_base.png')
    
    # ===== EXPLORE SIDEBAR =====
    print("\n" + "=" * 60)
    print("PHASE 3: SIDEBAR MENU EXPLORATION")
    
    # Find all sidebar menu items (clickable)
    menu_data = page.evaluate('''() => {
        const items = [];
        // Look for sidebar menu items with href or onclick
        document.querySelectorAll('[class*="sidebar"] a, [class*="sidebar"] li, [class*="menu-item"], [class*="nav-item"], [class*="el-menu-item"], aside a, aside li').forEach(el => {
            const text = (el.textContent || '').trim();
            const href = el.getAttribute('href') || '';
            const cls = (el.className || '').toString();
            if (text && text.length > 1 && text.length < 100 && !items.some(i => i.text === text)) {
                items.push({text, href, cls: cls.substring(0,60)});
            }
        });
        return items;
    }''')
    
    print(f"  Found {len(menu_data)} sidebar menu items:")
    for idx, m in enumerate(menu_data):
        flag = '🔴' if has_cn(m['text']) else '  '
        print(f"    {flag} [{idx}] {m['text'][:60]}")
    
    # Record Chinese menu items as issues
    for m in menu_data:
        if has_cn(m['text']):
            log_issue("侧边栏菜单", "menu-item", m['text'], "菜单项中文", "Translate menu item to English", 'dashboard_base.png')
    
    # ===== CLICK THROUGH MENU ITEMS =====
    print("\n" + "=" * 60)
    print("PHASE 4: CLICKING THROUGH PAGES")
    
    visited_texts = set()
    page_count = 0
    
    for idx, m in enumerate(menu_data):
        if page_count >= 20: break
        mt = m['text']
        if mt in visited_texts: continue
        if len(mt) < 2: continue
        visited_texts.add(mt)
        
        try:
            # Click the menu item - try multiple selectors
            clicked = False
            selectors = [
                f'text="{mt}"',
                f'[class*="sidebar"] text="{mt}"',
                f'aside text="{mt}"',
                f'li:has-text("{mt}")',
            ]
            for sel in selectors:
                try:
                    el = page.locator(sel).first
                    if el.count() > 0 and el.is_visible():
                        el.click()
                        clicked = True
                        break
                except: pass
            
            if not clicked:
                print(f"  ⚠️ Could not click: {mt}")
                continue
            
            page.wait_for_timeout(3000)
            
            # Close any dialog
            if close_dialog(page):
                page.wait_for_timeout(2000)
            
            page_name = mt.replace(' ', '_').replace('/', '_')[:30]
            sp_path = f'detail_{page_name}.png'
            page.screenshot(path=f'{OUT}/{sp_path}', full_page=True)
            
            # Scan for Chinese
            cn = scan_page(page, page_name)
            if cn:
                print(f"\n  📄 {mt} ({len(cn)} Chinese elements):")
                for ci in cn:
                    log_issue(mt, f"{ci['tag']}", ci['text'], "硬编码中文", f"Translate to English", sp_path)
            else:
                print(f"\n  ✅ {mt}: No Chinese text found")
            
            page_count += 1
            
        except Exception as e:
            print(f"  Error on {mt}: {e}")
    
    # ===== NAVIGATE TO COMMON SUB-PAGES =====
    print("\n" + "=" * 60)
    print("PHASE 5: SUB-PAGE NAVIGATION")
    
    # Try common sub-page patterns
    sub_paths = [
        '/web/render?fid=a2618b4f-1c7c-431e-9d27-c6fbf112ecdb&page=settings',
        '/web/render?fid=a2618b4f-1c7c-431e-9d27-c6fbf112ecdb&page=users',
        '/web/render?fid=a2618b4f-1c7c-431e-9d27-c6fbf112ecdb&page=devices',
    ]
    
    for sp in sub_paths:
        try:
            full = f'https://cloud.teld6.top{sp}' if not sp.startswith('http') else sp
            page.goto(full, timeout=15000)
            close_dialog(page)
            page.wait_for_timeout(2000)
            cn = scan_page(page, sp.replace('/','_')[:30])
            if cn:
                for ci in cn:
                    log_issue(sp[:40], f"{ci['tag']}", ci['text'], "硬编码中文", "Translate to English")
        except: pass
    
    browser.close()
    
    # ===== GENERATE REPORT =====
    print("\n" + "=" * 60)
    print(f"TOTAL UNIQUE ISSUES: {len(all_issues)}")
    
    # JSON
    with open(f'{OUT}/i18n_deep_issues.json', 'w') as f:
        json.dump(all_issues, f, ensure_ascii=False, indent=2)
    
    # Markdown report
    with open(f'{OUT}/i18n_deep_report.md', 'w') as f:
        f.write("# 🌐 国际化(i18n)翻译问题深度审查报告\n\n")
        f.write(f"**站点**: https://cloud.teld6.top/\n")
        f.write(f"**测试账号**: saasadmin\n")
        f.write(f"**目标语言**: English (en-US)\n")
        f.write(f"**审查方式**: 登录后逐页遍历所有菜单和功能页面\n")
        f.write(f"**发现问题**: {len(all_issues)} 处\n\n")
        
        # Stats
        types = {}
        for i in all_issues:
            types[i['type']] = types.get(i['type'], 0) + 1
        
        f.write("## 📊 问题分类\n\n")
        f.write("| 问题类型 | 数量 |\n|---|---|\n")
        for k,v in sorted(types.items(), key=lambda x:-x[1]):
            f.write(f"| {k} | {v} |\n")
        
        # By page
        pages = {}
        for i in all_issues:
            p = i['page']
            if p not in pages: pages[p] = []
            pages[p].append(i)
        
        f.write("\n## 🔍 详细问题清单\n\n")
        for pg, issues in sorted(pages.items()):
            f.write(f"### 📄 {pg} ({len(issues)} 处)\n\n")
            f.write("| # | 元素 | 原文 | 建议 |\n")
            f.write("|---|------|------|------|\n")
            for idx, i in enumerate(issues, 1):
                f.write(f"| {idx} | {i['element']} | `{i['text'][:80]}` | {i['suggestion']} |\n")
            f.write("\n")
    
    print(f"\n✅ Report: {OUT}/i18n_deep_report.md")
    print(f"📁 JSON:  {OUT}/i18n_deep_issues.json")
