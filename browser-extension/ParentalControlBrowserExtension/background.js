const RULE_API_URL = 'http://127.0.0.1:8787/api/blocklist';
const SYNC_ALARM = 'sync-block-rules';
const RULE_BASE_ID = 1000;

chrome.runtime.onInstalled.addListener(async () => {
  await ensureAlarm();
  await syncRules();
});

chrome.runtime.onStartup.addListener(async () => {
  await ensureAlarm();
  await syncRules();
});

chrome.alarms.onAlarm.addListener(async alarm => {
  if (alarm.name === SYNC_ALARM) {
    await syncRules();
  }
});

async function ensureAlarm() {
  await chrome.alarms.create(SYNC_ALARM, { periodInMinutes: 5 });
}

async function syncRules() {
  try {
    const response = await fetch(RULE_API_URL, { cache: 'no-store' });
    if (!response.ok) {
      throw new Error(`Rule API returned ${response.status}`);
    }

    const rules = await response.json();
    const previous = await chrome.storage.local.get(['ruleMap']);
    const previousIds = Array.isArray(previous.ruleMap) ? previous.ruleMap.map(rule => rule.id) : [];
    const ruleMap = [];
    const addRules = [];

    for (const [index, rule] of rules.entries()) {
      const id = RULE_BASE_ID + index;
      const redirectUrl = `${chrome.runtime.getURL('blocked.html')}#rule=${id}`;
      ruleMap.push({ id, rule: rule.rule, kind: rule.kind, redirectUrl });
      addRules.push({
        id,
        priority: 1,
        action: {
          type: 'redirect',
          redirect: { url: redirectUrl },
        },
        condition: {
          resourceTypes: ['main_frame', 'sub_frame'],
          urlFilter: buildUrlFilter(rule.rule, rule.kind),
        },
      });
    }

    await chrome.declarativeNetRequest.updateDynamicRules({
      removeRuleIds: previousIds,
      addRules,
    });

    await chrome.storage.local.set({
      ruleMap,
      lastSyncUtc: new Date().toISOString(),
      ruleCount: ruleMap.length,
    });
  } catch (error) {
    console.warn('ParentalControl rule sync failed:', error);
  }
}

function buildUrlFilter(rule, kind) {
  const trimmed = String(rule || '').trim();
  if (!trimmed) {
    return 'about:blank';
  }

  if (kind === 'domain') {
    return `||${trimmed}^`;
  }

  return trimmed;
}
