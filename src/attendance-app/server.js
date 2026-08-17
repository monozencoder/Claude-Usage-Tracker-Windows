const express = require('express');
const axios = require('axios');
const cheerio = require('cheerio');
const cookieParser = require('cookie-parser');
const cors = require('cors');
const path = require('path');
const fs = require('fs');
const querystring = require('querystring');

// Load config
const configPath = path.join(__dirname, 'config.json');
let config;
try {
  config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
} catch (e) {
  console.error('config.json の読み込みに失敗しました:', e.message);
  process.exit(1);
}

const app = express();
app.use(cors());
app.use(cookieParser());
app.use(express.json());
app.use(express.static(path.join(__dirname)));

// Axios instance with cookie jar
const api = axios.create({
  baseURL: config.kingOfTime.baseUrl,
  withCredentials: true,
  maxRedirects: 5,
  headers: {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8',
    'Accept-Language': 'ja,en-US;q=0.7,en;q=0.3',
  }
});

// Cookie storage
let sessionCookies = {};
let isLoggedIn = false;

// ====== Login ======
async function login() {
  const { companyCode, userId, password } = config.kingOfTime;

  if (!companyCode || !userId || !password) {
    throw new Error('config.json に会社コード・ユーザーID・パスワードを設定してください');
  }

  console.log('[King of Time] ログイン試行...');

  // Step 1: Get login page to obtain initial cookies
  const loginPage = await api.get('/admin', {
    params: { page_id: '/login/login_form' }
  });

  // Extract cookies from response
  const setCookies = loginPage.headers['set-cookie'] || [];
  setCookies.forEach(c => {
    const [keyVal] = c.split(';');
    const [key, val] = keyVal.split('=');
    if (key && val) sessionCookies[key.trim()] = val.trim();
  });

  // Build cookie string
  const cookieStr = Object.entries(sessionCookies).map(([k, v]) => `${k}=${v}`).join('; ');

  // Step 2: Submit login form
  const loginData = querystring.stringify({
    login_id: userId,
    login_password: password,
    company_code: companyCode,
    page_id: '/login/login_form',
    action_id: '1',
    call_from: 'login_form'
  });

  const loginRes = await api.post('/admin', loginData, {
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Cookie': cookieStr,
      'Origin': config.kingOfTime.baseUrl,
      'Referer': `${config.kingOfTime.baseUrl}/admin?page_id=/login/login_form`,
    }
  });

  // Update cookies after login
  const loginCookies = loginRes.headers['set-cookie'] || [];
  loginCookies.forEach(c => {
    const [keyVal] = c.split(';');
    const [key, val] = keyVal.split('=');
    if (key && val) sessionCookies[key.trim()] = val.trim();
  });

  // Check if login succeeded by looking for redirect or specific content
  const $ = cheerio.load(loginRes.data);
  const hasLoginForm = $('#login_id').length > 0;
  const hasLogoutLink = $('a:contains("ログアウト")').length > 0 || loginRes.data.includes('logout');

  if (hasLoginForm && !hasLogoutLink) {
    // Check for error message
    const errorMsg = $('.message_error, .error, .alert-error').text().trim();
    if (errorMsg) {
      throw new Error(`ログイン失敗: ${errorMsg}`);
    }
    // Maybe it redirected back to login
    throw new Error('ログインに失敗しました。会社コード・ユーザーID・パスワードを確認してください。');
  }

  isLoggedIn = true;
  console.log('[King of Time] ログイン成功');
  return { success: true, cookieStr: Object.entries(sessionCookies).map(([k, v]) => `${k}=${v}`).join('; ') };
}

// ====== Get attendance records ======
async function fetchAttendanceRecords(year, month) {
  if (!isLoggedIn) {
    await login();
  }

  const cookieStr = Object.entries(sessionCookies).map(([k, v]) => `${k}=${v}`).join('; ');

  try {
    // Try to access the attendance list page
    // King of Time uses page_id for navigation
    const params = {
      page_id: '/attendance/list',
      search_year: year,
      search_month: month,
    };

    const res = await api.get('/admin', {
      params: params,
      headers: {
        'Cookie': cookieStr,
        'Referer': `${config.kingOfTime.baseUrl}/admin`,
      }
    });

    const $ = cheerio.load(res.data);
    const records = [];

    // Parse attendance table - King of Time typically uses a table with class 'attendance-table'
    // or similar structure. We'll try multiple common patterns.
    const tables = $('table').filter((i, el) => {
      const html = $(el).html() || '';
      return html.includes('出勤') || html.includes('退勤') || html.includes('勤務');
    });

    if (tables.length > 0) {
      tables.first().find('tr').each((i, row) => {
        const cells = $(row).find('td, th');
        if (cells.length < 3) return;

        const rowData = [];
        cells.each((j, cell) => {
          rowData.push($(cell).text().trim());
        });

        // Try to identify date, clock-in, clock-out columns
        if (rowData.length >= 3) {
          const record = {
            date: rowData[0] || '',
            clockIn: rowData[1] || '',
            clockOut: rowData[2] || '',
            raw: rowData
          };
          records.push(record);
        }
      });
    }

    // If table parsing didn't work, try to find data in script tags or JSON
    if (records.length === 0) {
      const scripts = $('script').map((i, el) => $(el).html()).get();
      for (const script of scripts) {
        if (!script) continue;
        // Look for JSON data patterns
        const jsonMatch = script.match(/var\s+(attendanceData|recordData|data)\s*=\s*(\[[\s\S]*?\]);/i);
        if (jsonMatch) {
          try {
            const parsed = JSON.parse(jsonMatch[2]);
            return parsed;
          } catch (e) {
            // Not valid JSON, continue
          }
        }
      }
    }

    return records;

  } catch (error) {
    if (error.response && error.response.status === 302) {
      // Session expired, try to re-login
      isLoggedIn = false;
      return await fetchAttendanceRecords(year, month);
    }
    throw error;
  }
}

// ====== Get daily attendance detail ======
async function fetchDailyDetail(dateStr) {
  if (!isLoggedIn) {
    await login();
  }

  const cookieStr = Object.entries(sessionCookies).map(([k, v]) => `${k}=${v}`).join('; ');

  try {
    const res = await api.get('/admin', {
      params: {
        page_id: '/attendance/detail',
        target_date: dateStr,
      },
      headers: {
        'Cookie': cookieStr,
        'Referer': `${config.kingOfTime.baseUrl}/admin`,
      }
    });

    const $ = cheerio.load(res.data);
    const detail = {};

    // Extract detail fields
    const fields = [
      '出勤', '退勤', '休憩開始', '休憩終了',
      '勤務時間', '休憩時間', '残業時間',
      'ステータス', '備考'
    ];

    fields.forEach(field => {
      const label = $(`th:contains("${field}")`);
      if (label.length) {
        const value = label.next('td').text().trim() || label.closest('tr').find('td').text().trim();
        detail[field] = value;
      }
    });

    return detail;

  } catch (error) {
    if (error.response && error.response.status === 302) {
      isLoggedIn = false;
      return await fetchDailyDetail(dateStr);
    }
    throw error;
  }
}

// ====== API Routes ======

// Login endpoint
app.post('/api/login', async (req, res) => {
  try {
    // Allow overriding config credentials via request body
    if (req.body.companyCode) config.kingOfTime.companyCode = req.body.companyCode;
    if (req.body.userId) config.kingOfTime.userId = req.body.userId;
    if (req.body.password) config.kingOfTime.password = req.body.password;

    const result = await login();
    res.json(result);
  } catch (error) {
    res.status(401).json({ error: error.message });
  }
});

// Get attendance records for a specific month
app.get('/api/attendance', async (req, res) => {
  try {
    const year = req.query.year || new Date().getFullYear();
    const month = req.query.month || (new Date().getMonth() + 1);
    const records = await fetchAttendanceRecords(parseInt(year), parseInt(month));
    res.json({ records, year: parseInt(year), month: parseInt(month) });
  } catch (error) {
    res.status(500).json({ error: error.message });
  }
});

// Get daily detail
app.get('/api/attendance/detail', async (req, res) => {
  try {
    const date = req.query.date;
    if (!date) {
      return res.status(400).json({ error: 'date parameter is required (YYYY-MM-DD)' });
    }
    const detail = await fetchDailyDetail(date);
    res.json({ date, detail });
  } catch (error) {
    res.status(500).json({ error: error.message });
  }
});

// Get login status
app.get('/api/status', (req, res) => {
  res.json({
    loggedIn: isLoggedIn,
    companyCode: config.kingOfTime.companyCode ? '***' : '',
    hasCredentials: !!(config.kingOfTime.companyCode && config.kingOfTime.userId && config.kingOfTime.password)
  });
});

// Update config
app.post('/api/config', (req, res) => {
  const { companyCode, userId, password } = req.body;
  if (companyCode !== undefined) config.kingOfTime.companyCode = companyCode;
  if (userId !== undefined) config.kingOfTime.userId = userId;
  if (password !== undefined) config.kingOfTime.password = password;

  // Save to config file
  fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
  isLoggedIn = false; // Force re-login

  res.json({ success: true });
});

// ====== Start server ======
const PORT = config.server.port || 3001;
app.listen(PORT, () => {
  console.log(`========================================`);
  console.log(`  勤怠管理ダッシュボード (King of Time連携)`);
  console.log(`  http://localhost:${PORT}`);
  console.log(`========================================`);
  console.log(`  config.json に認証情報を設定してから`);
  console.log(`  http://localhost:${PORT}/api/login にPOSTしてください`);
  console.log(`========================================`);
});
