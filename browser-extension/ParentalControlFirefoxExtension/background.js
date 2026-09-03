const api = globalThis.browser ?? globalThis.chrome;
const RULE_API_URL = 'http://127.0.0.1:8787/api/blocklist';
const SYNC_ALARM = 'sync-block-rules';
const BLOCK_PAGE = api.runtime.getURL('blocked.html');
let currentRules = [];

api.runtime.onInstalled.addListener(async () => {
  await ensureAlarm();
  await syncRules();
});

api.runtime.onStartup.addListener(async () => {
  await ensureAlarm();
  await syncRules();
});

api.alarms.onAlarm.addListener(async alarm => {
  if (alarm.name === SYNC_ALARM) {
    await syncRules();
  }
});

if (api.webRequest?.onBeforeRequest) {
  api.webRequest.onBeforeRequest.addListener(
    details => {
      const matched = findMatchingRule(details.url);
      if (!matched) {
        return {};
      }

      return { redirectUrl: `${BLOCK_PAGE}#rule=${matched.id}` };
    },
    { urls: ['<all_urls>'], types: ['main_frame', 'sub_frame'] },
    ['blocking']
  );
}

async function ensureAlarm() {
  await api.alarms.create(SYNC_ALARM, { periodInMinutes: 5 });
}

async function syncRules() {
  try {
    const response = await fetch(RULE_API_URL, { cache: 'no-store' });
    if (!response.ok) {
      throw new Error(`Rule API returned ${response.status}`);
    }

    const rules = await response.json();
    currentRules = Array.isArray(rules)
      ? rules.map((rule, index) => ({
          id: 1000 + index,
          rule: rule.rule,
          kind: rule.kind,
        }))
      : [];

    await api.storage.local.set({
      ruleMap: currentRules,
      lastSyncUtc: new Date().toISOString(),
      ruleCount: currentRules.length,
    });
  } catch (error) {
    console.warn('ParentalControl rule sync failed:', error);
  }
}

function findMatchingRule(url) {
  const normalizedUrl = String(url || '');
  let parsed;

  try {
    parsed = new URL(normalizedUrl);
  } catch {
    return null;
  }

  const host = parsed.hostname.toLowerCase();
  const fullUrl = normalizedUrl.toLowerCase();

  for (const rule of currentRules) {
    const text = String(rule.rule || '').trim().toLowerCase();
    if (!text) {
      continue;
    }

    if (rule.kind === 'domain') {
      if (host === text || host.endsWith(`.${text}`)) {
        return rule;
      }
      continue;
    }

    if (fullUrl.includes(text)) {
      return rule;
    }
  }

  return null;
}
