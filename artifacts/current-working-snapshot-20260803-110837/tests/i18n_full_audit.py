"""Full i18n audit - Navigate all pages and find all translation issues."""
from playwright.sync_api import sync_playwright, expect
import json, os, re

OUTPUT_DIR = "/Users/guoqingzhou/QianYuan.AgenticFramework/tests/i18n_output"
os.makedirs(OUTPUT_DIR, exist_ok=True)

all_issues = []

def add_issue(page_name, element_type, selector, original_text, issue_type, suggestion=None, screenshot_path=None):
    issue = {
        "page": page_name,
        "element": element_type,
        "selector": selector,
        "original_text": original_text,
        "issue_type": issue_type,
        "suggestion": suggestion,
        "screenshot": screenshot_path
    }
    all_issues.append(issue)
    print(f"  [{issue_type}] {page_name} | {element_type}: '{original_text[:80]}'")

def extract_visible_text(page):
    return page.evaluate("""() => {
        const results = [];
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null, false);
        let node;
        while (node = walker.nextNode()) {
            const text = node.textContent.trim();
            const parent = node.parentElement;
            if (!text || !parent || text.length > 300) continue;
            const style = window.getComputedStyle(parent);
            if (style.display === 'none' || style.visibility === 'hidden') continue;
            const rect = parent.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) continue;
            results.push({
                text: text,
                tag: parent.tagName.toLowerCase(),
                cls: (parent.className || '').toString().substring(0, 120),
                id: parent.id || ''
            });
        }
        return results;
    }""")

def check_chinese_in_text(text):
    if not text:
        return False
    if text.startswith('http') or text.endswith('.js') or text.endswith('.css'):
        return False
    return bool(re.search(r'[\u4e00-\u9fff]', text))

def has_en(text):
    return bool(re.search(r'[a-zA-Z]{4,}', text))

def analyze_page(page, page_name, screenshot_prefix):
    """Analyze a single page for i18n issues."""
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(2000)
    
    ss_path = f'{OUTPUT_DIR}/{screenshot_prefix}_{page_name.replace("/", "_")[:40]}.png'
    page.screenshot(path=ss_path, full_page=True)
    
    texts = extract_visible_text(page)
    issues_on_page = 0
    
    # Check each text element
    for t in texts:
        txt = t.get('text', '')
        if len(txt) < 1 or len(txt) > 250:
            continue
        tag = t.get('tag', '')
        if tag in ('script', 'style'):
            continue
            
        if check_chinese_in_text(txt):
            label = f".{t['cls'][:50]}" if t['cls'] else ''
            add_issue(page_name, tag, label, txt,
                     "硬编码中文", suggestion="需要英文翻译", screenshot_path=ss_path)
            issues_on_page += 1
        
        if check_chinese_in_text(txt) and has_en(txt):
            label = f".{t['cls'][:50]}" if t['cls'] else ''
            add_issue(page_name, f"{tag}(mixed)", label, txt,
                     "中英文混杂", suggestion="统一使用英文", screenshot_path=ss_path)
            issues_on_page += 1
    
    # Check visible inputs
    inputs = page.locator('input:visible, textarea:visible, select:visible').all()
    for inp in inputs:
        try:
            ph = inp.get_attribute('placeholder') or ''
            if check_chinese_in_text(ph):
                add_issue(page_name, "input[placeholder]", f"placeholder='{ph}'", ph,
                         "placeholder未翻译", suggestion="Translate placeholder to English", screenshot_path=ss_path)
                issues_on_page += 1
            
            lbl = inp.evaluate("el => el.closest('label')?.textContent?.trim() || ''") or ''
            if check_chinese_in_text(lbl):
                add_issue(page_name, "label", "", lbl,
                         "表单标签未翻译", suggestion="Translate label to English", screenshot_path=ss_path)
                issues_on_page += 1
        except:
            pass
    
    # Check visible buttons
    buttons = page.locator('button:visible, [role="button"]:visible').all()
    for btn in buttons:
        try:
            txt = btn.inner_text().strip()
            if check_chinese_in_text(txt):
                add_issue(page_name, "button", "", txt,
                         "按钮文本未翻译", suggestion="Translate button to English", screenshot_path=ss_path)
                issues_on_page += 1
        except:
            pass
    
    # Check table headers
    table_headers = page.locator('th:visible, [role="columnheader"]:visible').all()
    for th in table_headers:
        try:
            txt = th.inner_text().strip()
            if check_chinese_in_text(txt):
                add_issue(page_name, "table-header", "", txt,
                         "表格列头未翻译", suggestion="Translate header to English", screenshot_path=ss_path)
                issues_on_page += 1
        except:
            pass
    
    return issues_on_page

with sync_playwright() as p:
    browser = p.chromium.launch(
        headless=True,
        args=['--no-sandbox', '--disable-setuid-sandbox', '--proxy-server=']
    )
    context = browser.new_context(viewport={'width': 1920, 'height': 1080}, locale='en-US')
    page = context.new_page()
    
    print("=" * 70)
    print("PHASE 1: LOGIN PAGE ANALYSIS")
    
    page.goto('https://cloud.teld6.top/web/', timeout=60000)
    page.wait_for_timeout(5000)
    
    # Close dialog if exists
    try:
        ok_btn = page.locator('button:has-text("确定"):visible').first
        if ok_btn.is_visible():
            print("Closing session-expired dialog...")
            try:
                dialog_title = page.locator('.dialog-title:visible, .modal-title:visible').first.inner_text()
            except:
                dialog_title = "提示信息"
            add_issue("登录弹窗", "dialog-title", "", dialog_title,
                     "弹窗标题未翻译", suggestion="Translate to 'Information'")
            add_issue("登录弹窗", "button", "", "确定",
                     "按钮文本未翻译", suggestion="Translate to 'OK'")
            ok_btn.click()
            page.wait_for_timeout(2000)
    except:
        pass
    
    # Now on login page
    current_url = page.url
    print(f"  URL: {current_url}")
    print(f"  Title: {page.title()}")
    
    # Screenshot login page
    page.screenshot(path=f'{OUTPUT_DIR}/p0_login_form.png', full_page=True)
    
    # Analyze login page text
    print("\n  Analyzing login page text...")
    login_texts = extract_visible_text(page)
    login_chinese = [t for t in login_texts if check_chinese_in_text(t['text'])]
    login_mixed = [t for t in login_texts if check_chinese_in_text(t['text']) and has_en(t['text'])]
    
    print(f"  Login page: {len(login_texts)} text nodes, {len(login_chinese)} contain Chinese")
    
    if login_chinese:
        print("  Chinese text on login page:")
        for t in login_chinese:
            print(f"    [{t['tag']}] {t['text'][:120]}")
    
    # Analyze login page
    analyze_page(page, "login_page", "p0")
    
    # PERFORM LOGIN - find visible inputs only
    print("\n" + "=" * 70)
    print("PHASE 2: PERFORMING LOGIN")
    
    visible_inputs = page.locator('input:visible').all()
    print(f"  Visible inputs: {len(visible_inputs)}")
    
    username_inp = None
    password_inp = None
    
    for inp in visible_inputs:
        try:
            tp = inp.get_attribute('type') or 'text'
            nm = inp.get_attribute('name') or ''
            ph = inp.get_attribute('placeholder') or ''
            pid = inp.get_attribute('id') or ''
            
            if tp == 'password' and not password_inp:
                password_inp = inp
            elif tp != 'password' and not username_inp and (nm or ph):
                username_inp = inp
        except:
            pass
    
    # More precise: use placeholder hints
    for inp in visible_inputs:
        try:
            ph = inp.get_attribute('placeholder') or ''
            if ('user' in ph.lower() or 'phone' in ph.lower() or 'email' in ph.lower()) and not username_inp:
                username_inp = inp
            if 'password' in ph.lower() and not password_inp:
                password_inp = inp
        except:
            pass
    
    if username_inp and password_inp:
        print(f"  Username input placeholder: '{username_inp.get_attribute('placeholder')}'")
        print(f"  Password input placeholder: '{password_inp.get_attribute('placeholder')}'")
        username_inp.fill('saasadmin')
        password_inp.fill('saasadminA?')
        print("  Credentials filled")
        
        # Check for "Remember me" checkbox or similar
        try:
            remember = page.locator('input[type="checkbox"]:visible, [class*="check"]:visible').first
            remember_text = remember.evaluate("el => el.closest('label')?.textContent?.trim() || ''")
            if check_chinese_in_text(remember_text):
                add_issue("登录页", "checkbox-label", "", remember_text,
                         "记住我/协议文本未翻译", suggestion="Translate to English")
        except:
            pass
        
        # Find login button
        login_btn = page.locator('button:visible:has-text("Sign"), button:visible:has-text("Login"), button:visible:has-text("登录"), button[type="submit"]:visible').first
        if login_btn:
            btn_text = login_btn.inner_text()
            print(f"  Login button text: '{btn_text}'")
            if check_chinese_in_text(btn_text):
                add_issue("登录页", "button", "", btn_text,
                         "登录按钮未翻译", suggestion="Translate to 'Sign In'")
            login_btn.click()
            print("  Login clicked, waiting...")
            page.wait_for_timeout(10000)
            page.wait_for_load_state('networkidle')
        else:
            print("  WARNING: Could not find login button!")
    else:
        print("  WARNING: Could not find login fields!")
    
    # Check login result
    post_login_url = page.url
    print(f"  Post-login URL: {post_login_url}")
    page.screenshot(path=f'{OUTPUT_DIR}/p1_post_login.png', full_page=True)
    
    # Check for error messages
    error_msgs = page.locator('.error:visible, [class*="error"]:visible, [class*="message"]:visible, .el-message:visible, [role="alert"]:visible').all()
    for em in error_msgs:
        try:
            txt = em.inner_text().strip()
            if txt and len(txt) < 200:
                if check_chinese_in_text(txt):
                    add_issue("登录错误", "error-message", "", txt,
                             "错误信息未翻译", suggestion="Translate error to English")
                print(f"  Error message: '{txt}'")
        except:
            pass
    
    # Analyze dashboard
    print("\n" + "=" * 70)
    print("PHASE 3: DASHBOARD ANALYSIS")
    analyze_page(page, "dashboard", "p_dash")
    
    # Explore navigation - discover all menu links
    print("\n" + "=" * 70)
    print("PHASE 4: EXPLORING ALL PAGES")
    
    # Get the full navigation structure
    nav_data = page.evaluate("""() => {
        const menus = [];
        const seen = new Set();
        
        // Look for sidebar menu items
        document.querySelectorAll('a[href], [class*="menu"], [class*="nav"], [role="menuitem"], [class*="tab"], [class*="link"]').forEach(el => {
            const text = (el.textContent || '').trim().substring(0, 100);
            const href = el.getAttribute('href') || '';
            const key = (text + '|' + href).substring(0, 200);
            
            if (!text || text.length < 2 || seen.has(key)) return;
            
            // Skip pure icon elements
            if (text.length < 2 && el.querySelector('svg, img, i, [class*="icon"]')) return;
            
            seen.add(key);
            menus.push({
                text: text,
                href: href,
                tag: el.tagName.toLowerCase(),
                cls: (el.className || '').toString().substring(0, 100),
                rect: (() => {
                    const r = el.getBoundingClientRect();
                    return {x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height)};
                })()
            });
        });
        
        // Sort by position (y first, then x) to get proper menu order
        menus.sort((a, b) => a.rect.y - b.rect.y || a.rect.x - b.rect.x);
        
        return menus.filter(m => m.rect.y > 0 && m.rect.h > 5);
    }""")
    
    print(f"  Found {len(nav_data)} navigation elements")
    
    # Filter for sidebar/main nav
    nav_items = [n for n in nav_data if n['href'] and not n['href'].startswith('#')]
    
    # Group by Y position to identify sidebar vs top bar
    for i, item in enumerate(nav_items[:50]):
        status = "🔗" if item['href'] else "  "
        print(f"  {status} [{item['tag']}@{item['rect']['y']}] {item['text'][:60]}")
    
    # Try to click through sidebar items
    visited_urls = set()
    visited_urls.add(post_login_url)
    
    sidebar_items = [n for n in nav_items if n['rect']['x'] < 500]  # Left side navigation
    
    explore_count = 0
    for item in sidebar_items:
        if explore_count >= 15:
            break
            
        href = item['href']
        if not href or href in visited_urls:
            continue
        
        # Build full URL
        if href.startswith('http'):
            full_url = href
        elif href.startswith('/'):
            full_url = f'https://cloud.teld6.top{href}'
        else:
            full_url = f'https://cloud.teld6.top/web/{href}'
        
        visited_urls.add(href)
        page_name = item['text'][:30].replace(' ', '_').replace('/', '_')
        
        try:
            print(f"\n  → Visiting [{page_name}] {full_url}")
            page.goto(full_url, timeout=30000)
            
            # Check Chinese in navigation text
            if check_chinese_in_text(item['text']):
                add_issue("导航菜单", "nav-item", "", item['text'],
                         "菜单项未翻译", suggestion="Translate to English")
            
            analyze_page(page, page_name, f"p_e{explore_count}")
            explore_count += 1
        except Exception as e:
            print(f"    Could not visit: {e}")
    
    # Also try to click on tabs within the page
    print("\n" + "=" * 70)
    print("PHASE 5: EXPLORING TABS AND SUB-PAGES")
    
    page.goto(post_login_url, timeout=30000)
    page.wait_for_timeout(3000)
    
    tabs = page.locator('[class*="tab"]:visible, [role="tab"]:visible, .el-tabs__item:visible').all()
    print(f"  Found {len(tabs)} tabs")
    for tab in tabs[:10]:
        try:
            txt = tab.inner_text().strip()
            if txt and check_chinese_in_text(txt):
                add_issue("标签页", "tab", "", txt,
                         "标签页标题未翻译", suggestion="Translate tab title to English")
                print(f"  Chinese tab: '{txt}'")
        except:
            pass
    
    # Check for dropdown menus
    dropdowns = page.locator('select:visible, [class*="dropdown"]:visible, [class*="select"]:visible').all()
    for dd in dropdowns[:5]:
        try:
            options = dd.locator('option').all()
            for opt in options:
                txt = opt.inner_text().strip()
                if check_chinese_in_text(txt):
                    add_issue("下拉菜单", "dropdown-option", "", txt,
                             "下拉选项未翻译", suggestion="Translate option to English")
        except:
            pass
    
    # Close browser
    browser.close()
    
    # Generate report
    print("\n" + "=" * 70)
    print("PHASE 6: GENERATING REPORT")
    
    # Deduplicate issues
    seen_issues = set()
    unique_issues = []
    for issue in all_issues:
        key = (issue['page'], issue['original_text'], issue['issue_type'])
        if key not in seen_issues:
            seen_issues.add(key)
            unique_issues.append(issue)
    
    all_issues[:] = unique_issues
    
    with open(f'{OUTPUT_DIR}/i18n_issues.json', 'w', encoding='utf-8') as f:
        json.dump(all_issues, f, ensure_ascii=False, indent=2)
    
    # Generate markdown report
    with open(f'{OUTPUT_DIR}/i18n_report.md', 'w', encoding='utf-8') as f:
        f.write("# 国际化(i18n)翻译问题审查报告\n\n")
        f.write(f"**站点**: https://cloud.teld6.top/\n")
        f.write(f"**审查范围**: 登录页 + 仪表盘 + 所有可访问子页面\n")
        f.write(f"**目标语言**: 英文 (en-US)\n")
        f.write(f"**发现问题总数**: {len(all_issues)}\n\n")
        
        # Summary by type
        issue_types = {}
        for issue in all_issues:
            it = issue['issue_type']
            if it not in issue_types:
                issue_types[it] = []
            issue_types[it].append(issue)
        
        f.write("## 📊 问题统计\n\n")
        f.write("| 问题类型 | 数量 |\n")
        f.write("|---------|------|\n")
        for itype, issues in sorted(issue_types.items(), key=lambda x: -len(x[1])):
            f.write(f"| **{itype}** | {len(issues)} |\n")
        
        f.write("\n## 🔴 详细问题列表\n\n")
        
        # Group by page
        page_groups = {}
        for issue in all_issues:
            pg = issue['page']
            if pg not in page_groups:
                page_groups[pg] = []
            page_groups[pg].append(issue)
        
        for pg, issues in sorted(page_groups.items()):
            f.write(f"### 📄 {pg} ({len(issues)} issues)\n\n")
            for idx, issue in enumerate(issues, 1):
                f.write(f"#### #{idx}: {issue['issue_type']}\n")
                f.write(f"- **元素**: `{issue['element']}`\n")
                f.write(f"- **原文**: `{issue['original_text']}`\n")
                f.write(f"- **建议**: {issue['suggestion']}\n")
                if issue['screenshot']:
                    f.write(f"- **截图**: `{issue['screenshot'].split('/')[-1]}`\n")
                f.write("\n")
    
    print(f"\n✅ 完成！")
    print(f"📊 发现 {len(all_issues)} 个国际化翻译问题")
    print(f"📄 详细报告: {OUTPUT_DIR}/i18n_report.md")
    print(f"📁 JSON: {OUTPUT_DIR}/i18n_issues.json")
    print(f"📸 截图: {OUTPUT_DIR}/*.png")
