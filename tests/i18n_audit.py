"""Comprehensive i18n audit for cloud.teld6.top"""
from playwright.sync_api import sync_playwright
import json, os, re

OUTPUT_DIR = "/Users/guoqingzhou/QianYuan.AgenticFramework/tests/i18n_output"
os.makedirs(OUTPUT_DIR, exist_ok=True)

all_issues = []

def add_issue(page_name, element, selector, text, issue_type, suggestion, screenshot):
    key = (page_name, text, issue_type)
    for existing in all_issues:
        if (existing['page'], existing['original_text'], existing['issue_type']) == key:
            return
    all_issues.append({
        "page": page_name, "element": element, "selector": selector,
        "original_text": text, "issue_type": issue_type,
        "suggestion": suggestion, "screenshot": screenshot
    })
    print(f"  [{issue_type}] {page_name} / {element}: '{text[:80]}'")

def has_cn(text):
    return bool(text and re.search(r'[\u4e00-\u9fff]', text) and 
                not text.startswith('http') and not text.endswith('.js') and not text.endswith('.css'))

def has_en(text):
    return bool(text and re.search(r'[a-zA-Z]{4,}', text))

def get_all_text(page):
    return page.evaluate("""() => {
        const r = [];
        const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null, false);
        let n;
        while (n = w.nextNode()) {
            const t = n.textContent.trim();
            const p = n.parentElement;
            if (!t || !p || t.length > 300) continue;
            if (t.length < 2) continue;
            const s = window.getComputedStyle(p);
            if (s.display === 'none' || s.visibility === 'hidden') continue;
            const rc = p.getBoundingClientRect();
            if (rc.width === 0 || rc.height === 0) continue;
            r.push({text: t, tag: p.tagName.toLowerCase(), cls: (p.className||'').toString().substring(0,120), id: p.id||''});
        }
        return r;
    }""")

def analyze_page(page, name, pfx):
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(2000)
    sp = f'{OUTPUT_DIR}/{pfx}_{name.replace("/","_")[:40]}.png'
    page.screenshot(path=sp, full_page=True)
    
    texts = get_all_text(page)
    count = 0
    for t in texts:
        txt = t['text']
        if t['tag'] in ('script','style','svg','path','circle','rect'): continue
        if len(txt) < 2: continue
        
        if has_cn(txt):
            add_issue(name, t['tag'], t['cls'][:60], txt, "硬编码中文", "Translate to English", sp)
            count += 1
        if has_cn(txt) and has_en(txt):
            add_issue(name, f"{t['tag']}(mixed)", t['cls'][:60], txt, "中英文混杂", "Use consistent language", sp)
            count += 1
    
    # Placeholders
    for inp in page.locator('input:visible, textarea:visible').all():
        try:
            ph = inp.get_attribute('placeholder') or ''
            if has_cn(ph):
                add_issue(name, "placeholder", "", ph, "placeholder中文", "Translate placeholder", sp); count+=1
        except: pass
    
    # Buttons
    for btn in page.locator('button:visible, [role="button"]:visible').all():
        try:
            txt = btn.inner_text().strip()
            if has_cn(txt):
                add_issue(name, "button", "", txt, "按钮中文", "Translate button", sp); count+=1
        except: pass
    
    # Table headers
    for th in page.locator('th:visible, [role="columnheader"]:visible').all():
        try:
            txt = th.inner_text().strip()
            if has_cn(txt):
                add_issue(name, "table-header", "", txt, "表头中文", "Translate header", sp); count+=1
        except: pass
    
    # Modal/dialog
    for dlg in page.locator('.dialog:visible, .modal:visible, [role="dialog"]:visible, [class*="modal"]:visible').all():
        try:
            title = dlg.locator('h1,h2,h3,h4,[class*="title"]').first
            if title:
                txt = title.inner_text().strip()
                if has_cn(txt):
                    add_issue(name, "dialog-title", "", txt, "弹窗标题中文", "Translate dialog title", sp); count+=1
        except: pass
    
    return count, sp

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--no-sandbox','--proxy-server='])
    ctx = browser.new_context(viewport={'width':1920,'height':1080}, locale='en-US', bypass_csp=True)
    page = ctx.new_page()
    
    # === LOGIN ===
    print("="*60)
    print("LOGIN")
    page.goto('https://cloud.teld6.top/web/', timeout=60000)
    page.wait_for_timeout(5000)
    
    # Close dialog
    try:
        ok = page.locator('button:has-text("确定"):visible')
        if ok.count() > 0:
            try: 
                t = page.locator('[class*="title"]:visible').first.inner_text()
                if has_cn(t): add_issue("登录弹窗","dialog-title","",t,"弹窗中文","Translate title",None)
            except: pass
            add_issue("登录弹窗","button","","确定","按钮中文","Translate OK button",None)
            ok.first.click(); page.wait_for_timeout(2000)
    except: pass
    
    # Login
    page.fill('input[placeholder*="Phone, email"]', 'saasadmin')
    page.fill('input[placeholder="Password"]', 'saasadminA?')
    page.wait_for_timeout(500)
    page.keyboard.press('Enter')
    page.wait_for_timeout(8000)
    page.wait_for_load_state('networkidle')
    
    print(f"  URL: {page.url}")
    
    # === EXPLORE ALL PAGES ===
    print("="*60)
    print("EXPLORING PAGES")
    
    discovered_urls = set()
    base = 'https://cloud.teld6.top'
    
    # Scan the current page for all navigation links
    all_links = page.evaluate("""() => {
        const links = new Set();
        document.querySelectorAll('a[href]').forEach(a => {
            const h = a.getAttribute('href');
            if (h && !h.startsWith('#') && !h.startsWith('javascript') && !h.startsWith('mailto') && !h.startsWith('tel')) {
                links.add(h);
            }
        });
        // Also check router links
        document.querySelectorAll('[data-url], [to], [data-href]').forEach(el => {
            ['data-url','to','data-href'].forEach(attr => {
                const v = el.getAttribute(attr);
                if (v && !v.startsWith('#')) links.add(v);
            });
        });
        return Array.from(links);
    }""")
    
    print(f"  Found {len(all_links)} unique URLs")
    
    # Navigate to each link
    visited = set()
    for link in all_links:
        if len(visited) >= 30: break
        
        full = link if link.startswith('http') else f'{base}{link}' if link.startswith('/') else f'{base}/{link}'
        if full in visited: continue
        visited.add(full)
        
        # Skip external links
        if 'cloud.teld6.top' not in full: continue
        # Skip logout
        if 'logout' in full.lower() or 'exit' in full.lower(): continue
        # Skip static files
        if full.endswith(('.js','.css','.png','.jpg','.svg','.ico','.woff','.woff2','.ttf')): continue
        
        pname = full.replace(base, '').replace('/','_')[:40]
        try:
            print(f"\n  → {pname}")
            page.goto(full, timeout=20000)
            cnt, sp = analyze_page(page, pname, 'p')
            if cnt == 0:
                print(f"    ✓ Clean (no Chinese text)")
        except Exception as e:
            print(f"    ✗ Failed: {e}")
    
    # Also try common paths
    common = ['/web/','/web/dashboard','/web/home','/web/settings','/web/profile',
              '/web/devices','/web/alerts','/web/logs','/web/reports','/web/monitor',
              '/web/network','/web/security','/web/metrics','/web/systems','/web/agents',
              '/web/inventory','/web/management','/web/config','/web/tools']
    for path in common:
        full = f'{base}{path}'
        if full in visited: continue
        visited.add(full)
        try:
            print(f"\n  → {path}")
            page.goto(full, timeout=15000)
            cnt, sp = analyze_page(page, path.replace('/','_')[:40], 'c')
            if cnt == 0: print(f"    ✓ Clean")
        except: pass
    
    browser.close()
    
    # === GENERATE REPORT ===
    print("\n"+"="*60)
    print(f"TOTAL ISSUES: {len(all_issues)}")
    
    # Save JSON
    with open(f'{OUTPUT_DIR}/i18n_issues.json','w') as f:
        json.dump(all_issues, f, ensure_ascii=False, indent=2)
    
    # Save MD report
    with open(f'{OUTPUT_DIR}/i18n_report.md','w') as f:
        f.write("# 🌐 国际化(i18n)翻译问题审查报告\n\n")
        f.write(f"**站点**: https://cloud.teld6.top/\n")
        f.write(f"**测试账号**: saasadmin\n")
        f.write(f"**目标语言**: English (en-US)\n")
        f.write(f"**发现问题**: {len(all_issues)} 处\n\n")
        
        # Stats
        types = {}
        for i in all_issues:
            types[i['issue_type']] = types.get(i['issue_type'], 0) + 1
        
        f.write("## 📊 问题分类统计\n\n")
        f.write("| 问题类型 | 数量 |\n|---|---|\n")
        for k,v in sorted(types.items(), key=lambda x:-x[1]):
            f.write(f"| {k} | {v} |\n")
        
        # By page
        pages = {}
        for i in all_issues:
            pg = i['page']
            if pg not in pages: pages[pg] = []
            pages[pg].append(i)
        
        f.write("\n## 🔴 详细问题（按页面分组）\n\n")
        for pg, issues in sorted(pages.items()):
            f.write(f"### 📄 {pg} ({len(issues)} issues)\n\n")
            for idx, i in enumerate(issues, 1):
                f.write(f"**#{idx}** `[{i['issue_type']}]`\n")
                f.write(f"- 元素: `{i['element']}`\n")
                f.write(f"- 原文: `{i['original_text']}`\n")
                f.write(f"- 建议: {i['suggestion']}\n")
                if i['screenshot']:
                    f.write(f"- 截图: `{i['screenshot'].split('/')[-1]}`\n")
                f.write("\n")
    
    print(f"\n✅ Report: {OUTPUT_DIR}/i18n_report.md")
    print(f"📁 JSON:  {OUTPUT_DIR}/i18n_issues.json")
