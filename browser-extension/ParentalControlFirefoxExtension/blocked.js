const api = globalThis.browser ?? globalThis.chrome;
const ruleText = document.getElementById('ruleText');
const ruleBadge = document.getElementById('ruleBadge');

async function loadRule() {
  const params = new URLSearchParams(window.location.hash.replace(/^#/, ''));
  const ruleId = Number(params.get('rule'));

  if (!Number.isFinite(ruleId) || ruleId <= 0) {
    ruleText.textContent = 'The blocked URL matched a ParentalControl rule.';
    return;
  }

  const state = await api.storage.local.get(['ruleMap']);
  const ruleMap = Array.isArray(state.ruleMap) ? state.ruleMap : [];
  const match = ruleMap.find(entry => entry.id === ruleId);

  if (!match) {
    ruleText.textContent = `Rule ${ruleId} matched a ParentalControl block rule.`;
    return;
  }

  ruleBadge.textContent = `${match.kind === 'domain' ? 'Domain' : 'Fragment'} rule`;
  ruleText.textContent = `Blocked rule: ${match.rule}`;
}

loadRule().catch(error => {
  console.warn('Unable to read blocked rule details:', error);
  ruleText.textContent = 'This site is blocked by ParentalControl.';
});
