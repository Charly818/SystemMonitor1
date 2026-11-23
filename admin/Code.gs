/**
 * АДМІН-ПАНЕЛЬ: приймає анонімні сигнали від програми й веде таблицю користувачів.
 * Таблиця приватна: її бачите лише ви (власник Google-акаунта).
 *
 * Як налаштувати (один раз):
 * 1. Створіть нову Google Таблицю (sheets.new).
 * 2. Розширення → Apps Script. Видаліть увесь код і вставте цей файл.
 * 3. Угорі виберіть функцію setupDashboard і натисніть «Виконати» (дозвольте доступ). З'являться аркуші «Панель» і «Користувачі».
 * 4. Розгорнути → Нове розгортання → тип «Веб-застосунок».
 *    Виконувати від імені: Я. Хто має доступ: Усі. Натисніть «Розгорнути» і скопіюйте URL веб-застосунку.
 * 5. На GitHub: репозиторій → Settings → Secrets and variables → Actions → вкладка Variables →
 *    New repository variable: ім'я STATS_URL, значення = скопійований URL.
 * 6. Випустіть нову версію програми (Actions → Run workflow). Далі користувачі, які дозволили статистику, з'являтимуться в таблиці.
 */

const SHEET_USERS = 'Користувачі';
const SHEET_DASH = 'Панель';

function usersSheet_() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  let sh = ss.getSheetByName(SHEET_USERS);
  if (!sh) {
    sh = ss.insertSheet(SHEET_USERS);
    sh.appendRow(['ID установки', 'Перший запуск', 'Останній сигнал', 'Версія', 'Windows', 'Мова', 'Кількість сигналів']);
    sh.setFrozenRows(1);
    sh.getRange('B:C').setNumberFormat('yyyy-mm-dd hh:mm');
  }
  return sh;
}

function out_(text) {
  return ContentService.createTextOutput(text);
}

// Сюди надходять сигнали від програми.
function doPost(e) {
  const lock = LockService.getScriptLock();
  lock.waitLock(15000);
  try {
    const d = JSON.parse(e.postData.contents);
    const id = String(d.id || '');
    if (id.length < 8 || id.length > 64) return out_('bad id');

    const sh = usersSheet_();
    const now = new Date();
    const last = sh.getLastRow();
    let row = -1;
    if (last > 1) {
      const ids = sh.getRange(2, 1, last - 1, 1).getValues();
      for (let i = 0; i < ids.length; i++) {
        if (ids[i][0] === id) { row = i + 2; break; }
      }
    }
    const version = String(d.version || '').slice(0, 20);
    const os = String(d.os || '').slice(0, 30);
    const lang = String(d.lang || '').slice(0, 20);

    if (row === -1) {
      sh.appendRow([id, now, now, version, os, lang, 1]);
    } else {
      const count = Number(sh.getRange(row, 7).getValue()) || 0;
      sh.getRange(row, 3, 1, 5).setValues([[now, version, os, lang, count + 1]]);
    }
    return out_('ok');
  } catch (err) {
    return out_('error');
  } finally {
    lock.releaseLock();
  }
}

// Відкрийте URL веб-застосунку в браузері, щоб перевірити, що все працює.
function doGet() {
  return out_('Системний монітор: сервер статистики працює.');
}

// Запустіть один раз: створює аркуш «Панель» зі зведенням.
function setupDashboard() {
  usersSheet_();
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  let sh = ss.getSheetByName(SHEET_DASH);
  if (!sh) sh = ss.insertSheet(SHEET_DASH, 0);
  sh.clear();
  const u = "'" + SHEET_USERS + "'";
  sh.getRange('A1').setValue('Системний монітор: користувачі').setFontSize(14).setFontWeight('bold');
  const rows = [
    ['Усього установок', '=COUNTA(' + u + '!A2:A)'],
    ['Активні за останню добу', '=COUNTIF(' + u + '!C2:C,">"&(NOW()-1))'],
    ['Активні за 7 днів', '=COUNTIF(' + u + '!C2:C,">"&(NOW()-7))'],
    ['Активні за 30 днів', '=COUNTIF(' + u + '!C2:C,">"&(NOW()-30))'],
    ['Нові за 7 днів', '=COUNTIF(' + u + '!B2:B,">"&(NOW()-7))'],
  ];
  sh.getRange(3, 1, rows.length, 2).setFormulas(rows);
  sh.getRange('A10').setValue('За версіями').setFontWeight('bold');
  sh.getRange('A11').setFormula('=IFERROR(QUERY(' + u + '!D2:D,"select D, count(D) where D is not null group by D order by count(D) desc label D \'Версія\', count(D) \'Кількість\'",0),"поки немає даних")');
  sh.setColumnWidth(1, 260);
  sh.setColumnWidth(2, 120);
}
