"""Step 1: Reconnaissance - Login and discover all pages/menus."""
from playwright.sync_api import sync_playwright
import json, os, re

OUTPUT_DIR = "/Users/guoqingzhou/QianYuan.AgenticFramework/tests/i18n_output"
os.makedirs(OUTPUT_DIR, exist_ok=True)

def extract_visible_text(page):
    """Extract all visible text elements with their text content."""
    texts = page.evaluate("""() => {
        const results = [];
        const walker = document.createTreeWalker(
            document.body,
            NodeFilter.SHOW_TEXT,
            null,
            false
        );
        let node;
        while (node = walker.nextNode()) {
            const text = node.textContent.trim();
            const parent = node.parentElement;
            if (!text || !parent) continue;
            const style = window.getComputedStyle(parent);
            if (style.display === 'none' || style.visibility === 'hidden') continue;
            const rect = parent.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) continue;
            const tag = parent.tagName.toLowerCase();
            const cls = parent.className || '';
            const id = parent.id || '';
            results.push({text: text.substring(0, 200), tag, cls: cls.substring(0, 80), id});
        }
        return results;
    }""")
    return texts

def find_chinese_text(texts):
    """Find text elements containing Chinese characters."""
    chinese = []
    for t in texts:
        if re.search(r'[\u4e00-\u9fff]', t['text']):
            chinese.append(t)
    return chinese

def find_mixed_lang_issues(texts):
    """Find text with mixed Chinese + English (potential i18n issues)."""
    issues = []
    for t in texts:
        has_cn = bool(re.search(r'[\u4e00-\u9fff]', t['text']))
        has_en = bool(re.search(r'[a-zA-Z]{3,}', t['text']))
        if has_cn and has_en:
            issues.append(t)
    return issues

def find_placeholder_issues(page):
    """Find input placeholders that might not be translated."""
    return page.evaluate("""() => {
        const inputs = document.querySelectorAll('input, textarea, select');
        const results = [];
        inputs.forEach(el => {
            const placeholder = el.getAttribute('placeholder') || '';
            const label = el.closest('label')?.textContent?.trim() || '';
            const ariaLabel = el.getAttribute('aria-label') || '';
            const name = el.getAttribute('name') || el.getAttribute('id') || '';
            if (placeholder || name) {
                results.push({name, placeholder, label: label.substring(0, 50), ariaLabel, tag: el.tagName});
            }
        });
        return results;
    }""")

with sync_playwright() as p:
    browser = p.chromium.launch(
        headless=True,
        args=['--no-sandbox', '--disable-setuid-sandbox', '--proxy-server=']
    )
    context = browser.new_context(
        viewport={'width': 1920, 'height': 1080},
        locale='en-US',
        bypass_csp=True
    )
    page = context.new_page()

    # Step 1: Navigate to login page (the site redirects to /web/)
    print("=" * 60)
    print("STEP 1: Navigating to login page...")
    try:
        page.goto('https://cloud.teld6.top/web/', timeout=60000)
        page.wait_for_load_state('networkidle')
        page.wait_for_timeout(5000)
    except Exception as e:
        print(f"Warning on first load: {e}")
        page.wait_for_timeout(10000)

    page.screenshot(path=f'{OUTPUT_DIR}/01_login_page.png', full_page=True)
    print("Screenshot saved: 01_login_page.png")
    print(f"Current URL: {page.url}")
    print(f"Page title: {page.title()}")

    # Extract all text on login page
    login_texts = extract_visible_text(page)
    chinese_login = find_chinese_text(login_texts)
    mixed_login = find_mixed_lang_issues(login_texts)
    
    print(f"\nLogin page: {len(login_texts)} text elements, {len(chinese_login)} contain Chinese")
    if chinese_login:
        print("Chinese text on login page:")
        for t in chinese_login[:20]:
            print(f"  [{t['tag']}] {t['text'][:100]}")
    
    if mixed_login:
        print("\nMixed-language text on login page:")
        for t in mixed_login[:10]:
            print(f"  [{t['tag']}] {t['text'][:120]}")

    # Inspect login form
    inputs = find_placeholder_issues(page)
    print(f"\nLogin page inputs: {len(inputs)}")
    for inp in inputs[:15]:
        print(f"  name={inp['name']} placeholder='{inp['placeholder']}' label='{inp['label']}'")

    # Step 2: Login
    print("\n" + "=" * 60)
    print("STEP 2: Attempting login...")

    login_success = False
    try:
        # Look at what input fields exist
        all_inputs = page.locator('input').all()
        print(f"Total input elements found: {len(all_inputs)}")
        for i, inp in enumerate(all_inputs):
            try:
                tp = inp.get_attribute('type') or 'text'
                ph = inp.get_attribute('placeholder') or ''
                nm = inp.get_attribute('name') or ''
                pid = inp.get_attribute('id') or ''
                print(f"  Input[{i}]: type={tp} name={nm} id={pid} placeholder='{ph}'")
            except:
                pass

        # Try different approaches
        username_field = None
        password_field = None
        
        # Try by placeholder
        for inp in all_inputs:
            try:
                ph = inp.get_attribute('placeholder') or ''
                tp = inp.get_attribute('type') or ''
                if tp == 'password':
                    password_field = inp
                elif any(kw in ph.lower() for kw in ['user', 'account', 'name', '邮箱', '手机', '用户名', '账号']):
                    username_field = inp
            except:
                pass

        # Fallback: first text input and first password
        if not username_field:
            text_inputs = [i for i in all_inputs if (i.get_attribute('type') or 'text') not in ('password', 'hidden', 'checkbox', 'radio')]
            if text_inputs:
                username_field = text_inputs[0]
        if not password_field:
            pw_inputs = [i for i in all_inputs if i.get_attribute('type') == 'password']
            if pw_inputs:
                password_field = pw_inputs[0]

        if username_field and password_field:
            username_field.fill('saasadmin')
            password_field.fill('saasadminA?')
            print(f"Filled username and password")
            page.wait_for_timeout(500)
            
            # Find login button
            login_btns = page.locator('button').all()
            print(f"Buttons found: {len(login_btns)}")
            for b in login_btns:
                try:
                    txt = b.inner_text()
                    print(f"  Button: '{txt}'")
                except:
                    pass
            
            # Try to click login button
            login_btn = page.locator('button:has-text("登录")').first
            if not login_btn.is_visible():
                login_btn = page.locator('button[type="submit"]').first
            if not login_btn.is_visible():
                login_btn = page.locator('button').last
            
            login_btn.click()
            print("Login button clicked, waiting for redirect...")
            page.wait_for_timeout(8000)
            page.wait_for_load_state('networkidle')
            
        else:
            print("Could not find login fields!")
            page.screenshot(path=f'{OUTPUT_DIR}/01b_login_no_fields.png', full_page=True)
            
    except Exception as e:
        print(f"Login error: {e}")
        page.screenshot(path=f'{OUTPUT_DIR}/01b_login_error.png', full_page=True)

    # Step 3: Check if logged in
    current_url = page.url
    print(f"\nCurrent URL after login: {current_url}")
    page.screenshot(path=f'{OUTPUT_DIR}/02_after_login.png', full_page=True)
    
    # Check for error messages
    error_selectors = ['.error', '.el-message--error', '.ant-message-error', '[class*="error"]', 
                       '[class*="message"]', '.toast', '.notification', '[role="alert"]']
    for sel in error_selectors:
        try:
            errs = page.locator(sel).all_text_contents()
            if errs and any(e.strip() for e in errs):
                print(f"Error via {sel}: {errs}")
        except:
            pass

    # Step 4: Discover all navigation menus
    print("\n" + "=" * 60)
    print("STEP 4: Discovering navigation structure...")
    
    menus = page.evaluate("""() => {
        const results = [];
        const seen = new Set();
        // Try various sidebar/nav selectors
        const selectors = [
            '.sidebar a', '.el-menu-item', '.ant-menu-item',
            'nav a', '.menu-item a', '[class*="sidebar"] a',
            '[class*="menu"] li', '.navbar a', '.nav a',
            '[role="menuitem"]', '.t-menu__item', '.tabs a',
            '[class*="nav"] a', '.submenu-title', '.layui-nav-item a',
            'aside a', '.left-menu a', '[class*="tab"]',
            '.el-submenu__title', '.ant-menu-submenu-title'
        ];
        selectors.forEach(sel => {
            document.querySelectorAll(sel).forEach(el => {
                const text = (el.textContent || '').trim();
                const href = el.getAttribute('href') || '';
                const cls = (el.className || '').toString();
                const key = text + href;
                if (text && text.length < 200 && !seen.has(key)) {
                    seen.add(key);
                    results.push({selector: sel, text, href, cls: cls.substring(0, 80)});
                }
            });
        });
        return results;
    }""")
    
    print(f"Found {len(menus)} navigation items:")
    for m in menus:
        status = "🔗" if m['href'] else "  "
        print(f"  {status} [{m['selector'][:30]}] {m['text'][:80]}")

    # Step 5: Extract full page content
    page_html = page.content()
    with open(f'{OUTPUT_DIR}/page_source.html', 'w') as f:
        f.write(page_html)
    
    # Save all text elements
    all_texts = extract_visible_text(page)
    with open(f'{OUTPUT_DIR}/all_visible_text.json', 'w') as f:
        json.dump(all_texts, f, ensure_ascii=False, indent=2)
    
    # Find all Chinese text
    chinese_all = find_chinese_text(all_texts)
    print(f"\nAll Chinese text on page: {len(chinese_all)} elements")
    for t in chinese_all[:50]:
        print(f"  [{t['tag']}] {t['text'][:120]}")
    
    # Mixed language issues
    mixed_all = find_mixed_lang_issues(all_texts)
    print(f"\nMixed-language text: {len(mixed_all)} elements")
    for t in mixed_all[:30]:
        print(f"  [{t['tag']}] {t['text'][:120]}")

    browser.close()
    print("\n" + "=" * 60)
    print("Reconnaissance complete! Check i18n_output/ directory.")
