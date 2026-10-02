// Replays the Firebase REST calls of Assets/CPW/Scripts/Online/*.cs against the local Auth + Realtime Database
// emulators, with Docs/firebase/database.rules.json loaded, for four anonymous players. Each call is built the way
// FirebaseClient.cs builds it: <db>/<path>.json?auth=<idToken>[&query], JSON bodies, {".sv":"timestamp"} server
// times, PUT / PATCH / POST / DELETE. Checks that what the game needs is allowed and that cheating is refused.
//
// Run (needs Node 18+ and Java 11+):   cd Tools/firebase_tests && npm install && npm test
// (npm test = firebase emulators:exec --project demo-cpw --only auth,database "node rules_test.js")

'use strict';

const NS = process.env.GCLOUD_PROJECT || 'demo-cpw';
const AUTH = 'http://' + (process.env.FIREBASE_AUTH_EMULATOR_HOST || '127.0.0.1:9099');
const DB = 'http://' + (process.env.FIREBASE_DATABASE_EMULATOR_HOST || '127.0.0.1:9000');
const API_KEY = 'fake-api-key';
const SV = { '.sv': 'timestamp' };   // Fb.ServerTime

let passed = 0;
const failures = [];

// ---------------------------------------------------------------- FirebaseClient.cs equivalents

async function send(method, url, body, contentType) {
  const init = { method, headers: {} };
  if (body !== undefined && body !== null) {
    init.body = body;
    init.headers['Content-Type'] = contentType;
  }
  const res = await fetch(url, init);
  const text = await res.text();
  let json = null;
  try { json = JSON.parse(text); } catch (e) { json = undefined; }
  return { ok: res.status >= 200 && res.status < 300, code: res.status, text, json };
}

/** SignUpRoutine: accounts:signUp with returnSecureToken. */
async function signUp(name) {
  const r = await send('POST', AUTH + '/identitytoolkit.googleapis.com/v1/accounts:signUp?key=' + encodeURIComponent(API_KEY),
    '{"returnSecureToken":true}', 'application/json');
  if (!r.ok || !r.json || !r.json.idToken) throw new Error('signUp failed: ' + r.text);
  return { name, uid: r.json.localId, idToken: r.json.idToken, refreshToken: r.json.refreshToken, expiresIn: +r.json.expiresIn };
}

/** RefreshRoutine: securetoken grant_type=refresh_token (form encoded). */
async function refresh(u) {
  const body = 'grant_type=refresh_token&refresh_token=' + encodeURIComponent(u.refreshToken);
  const r = await send('POST', AUTH + '/securetoken.googleapis.com/v1/token?key=' + encodeURIComponent(API_KEY), body,
    'application/x-www-form-urlencoded');
  return r;
}

/** DbRoutine: <root>/<path>.json?auth=<idToken>&<query>  (plus ns=, which only the emulator needs). */
function db(u, method, path, body, query) {
  path = path.replace(/^\/+|\/+$/g, '');
  let url = DB + '/' + path + '.json?' + (u ? 'auth=' + encodeURIComponent(u.idToken) + '&' : '') + 'ns=' + NS;
  if (query) url += '&' + query;
  // MiniJson.Serialize: every body is JSON, a lone string too ("abc" with quotes)
  return send(method, url, body === undefined || body === null ? null : JSON.stringify(body), 'application/json');
}
const get = (u, p, q) => db(u, 'GET', p, null, q);
const put = (u, p, b) => db(u, 'PUT', p, b);
const patch = (u, p, b) => db(u, 'PATCH', p, b);
const post = (u, p, b) => db(u, 'POST', p, b);
const del = (u, p) => db(u, 'DELETE', p);
/** Fb.Q: a quoted, URL escaped query value. */
const Q = s => encodeURIComponent('"' + s + '"');

/** Admin access (emulator only) to look at or age data, bypassing the rules. */
function admin(method, path, body, query) {
  let url = DB + '/' + path + '.json?ns=' + NS + (query ? '&' + query : '');
  const init = { method, headers: { Authorization: 'Bearer owner' } };
  if (body !== undefined) { init.body = JSON.stringify(body); init.headers['Content-Type'] = 'application/json'; }
  return fetch(url, init).then(async r => ({ ok: r.ok, code: r.status, json: JSON.parse((await r.text()) || 'null') }));
}

// ---------------------------------------------------------------- expectations

function allow(desc, r) {
  if (r.ok) { passed++; return r; }
  failures.push('SHOULD BE ALLOWED: ' + desc + ' -> ' + r.code + ' ' + r.text);
  return r;
}
function deny(desc, r) {
  if (!r.ok && (r.code === 401 || r.code === 403)) { passed++; return r; }
  failures.push('SHOULD BE DENIED:  ' + desc + ' -> ' + r.code + ' ' + (r.text || '').slice(0, 200));
  return r;
}
function check(desc, cond, detail) {
  if (cond) { passed++; return; }
  failures.push('CHECK FAILED:      ' + desc + (detail !== undefined ? ' -> ' + JSON.stringify(detail).slice(0, 300) : ''));
}

const isoWeek = d => {
  const t = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()));
  const dow = (t.getUTCDay() + 6) % 7;
  t.setUTCDate(t.getUTCDate() + 3 - dow);
  const start = Date.UTC(t.getUTCFullYear(), 0, 1);
  const week = Math.floor((t - start) / 86400000 / 7) + 1;
  return t.getUTCFullYear() + '-W' + String(week).padStart(2, '0');
};
const now = new Date();
const WEEK = isoWeek(now), LAST_WEEK = isoWeek(new Date(now - 7 * 86400000));
const MONTH = now.toISOString().slice(0, 7);
const TODAY = now.toISOString().slice(0, 10).replace(/-/g, '');
const sleep = ms => new Promise(r => setTimeout(r, ms));

// ---------------------------------------------------------------- scenarios

async function loadRules() {
  const file = require('path').join(__dirname, '..', '..', 'Docs', 'firebase', 'database.rules.json');
  const r = await fetch(DB + '/.settings/rules.json?ns=' + NS, { method: 'PUT', headers: { Authorization: 'Bearer owner' },
    body: require('fs').readFileSync(file, 'utf8') });
  if (!r.ok) throw new Error('the emulator refused ' + file + ': ' + (await r.text()));
  await admin('PUT', '', null);   // start from an empty database
}

async function main() {
  await loadRules();
  const [A, B, C, D] = [await signUp('Alice'), await signUp('Bob'), await signUp('Cleo'), await signUp('Dan')];
  check('four distinct anonymous users', new Set([A.uid, B.uid, C.uid, D.uid]).size === 4);
  const anon = null;

  // --- sign-in + token refresh (FirebaseClient.EnsureAuth)
  const rf = await refresh(A);
  check('token refresh returns id_token/refresh_token/user_id/expires_in',
    rf.ok && rf.json.id_token && rf.json.refresh_token && rf.json.user_id === A.uid && rf.json.expires_in, rf.json);
  if (rf.ok) { A.idToken = rf.json.id_token; A.refreshToken = rf.json.refresh_token; }
  const bad = await send('POST', AUTH + '/securetoken.googleapis.com/v1/token?key=x', 'grant_type=refresh_token&refresh_token=nonsense',
    'application/x-www-form-urlencoded');
  check('revoked refresh token answers 400 (client then signs up again)', bad.code === 400, bad.code);

  // --- FirebaseService.Init: users/{uid}/lastSeen = server time; response is the resolved number
  for (const u of [A, B, C, D]) {
    const r = allow('Init lastSeen ' + u.name, await put(u, 'users/' + u.uid + '/lastSeen', SV));
    check('lastSeen PUT answers a number (LearnServerTime)', typeof r.json === 'number' && r.json > 1e12, r.json);
  }
  deny('unauthenticated read of users', await get(anon, 'users/' + A.uid));
  deny('unauthenticated lastSeen', await put(anon, 'users/' + A.uid + '/lastSeen', SV));
  deny('lastSeen as a string', await put(A, 'users/' + A.uid + '/lastSeen', 'yesterday'));

  // --- cloud save (FlushProfile / PullProfile)
  const profile = { json: JSON.stringify({ displayName: 'Alice', level: 7, coins: 1234 }), name: 'Alice', updated: SV };
  allow('profile save', await put(A, 'users/' + A.uid + '/profile', profile));
  const pr = allow('profile load', await get(A, 'users/' + A.uid + '/profile'));
  check('profile json round trip', pr.json && pr.json.json === profile.json && typeof pr.json.updated === 'number', pr.json);
  deny('Bob writes Alice\'s profile', await put(B, 'users/' + A.uid + '/profile', profile));
  deny('Bob reads Alice\'s profile', await get(B, 'users/' + A.uid + '/profile'));
  deny('profile without json', await put(A, 'users/' + A.uid + '/profile', { name: 'x', updated: SV }));
  deny('unknown node under users/{uid}', await put(A, 'users/' + A.uid + '/coins', 999999));
  allow('a big profile (150 KB) still fits', await put(A, 'users/' + A.uid + '/profile', { json: 'x'.repeat(150000), name: 'A', updated: SV }));
  allow('profile save again', await put(A, 'users/' + A.uid + '/profile', profile));

  // --- leaderboards (SubmitStats / GetLeaderboard / Leaderboards.Get)
  const board = (u, xp, wins) => ({ name: u.name, level: 5, xp, wins, games: wins + 2, kills: wins, deaths: 1, damage: xp / 2,
    turns: 10, suicides: 0, shots: 12, updated: SV });
  const xps = { [A.uid]: 500, [B.uid]: 900, [C.uid]: 100, [D.uid]: 300 };
  for (const u of [A, B, C, D]) {
    allow('leaderboard all ' + u.name, await put(u, 'leaderboard/all/' + u.uid, board(u, xps[u.uid], 3)));
    allow('leaderboard week ' + u.name, await put(u, 'leaderboard/' + WEEK + '/' + u.uid, board(u, xps[u.uid] / 10, 1)));
    allow('leaderboard month ' + u.name, await put(u, 'leaderboard/' + MONTH + '/' + u.uid, board(u, xps[u.uid] / 5, 2)));
  }
  deny('Bob writes Alice\'s leaderboard entry', await put(B, 'leaderboard/all/' + A.uid, board(B, 99999, 99)));
  deny('leaderboard under a made-up period', await put(A, 'leaderboard/forever/' + A.uid, board(A, 1, 1)));
  deny('leaderboard entry without xp', await put(A, 'leaderboard/all/' + A.uid, { name: 'A', level: 1, wins: 1 }));
  deny('unauthenticated leaderboard read', await get(anon, 'leaderboard/all', 'orderBy=' + Q('xp') + '&limitToLast=50'));
  for (const period of ['all', WEEK, MONTH]) {
    for (const cat of ['xp', 'wins', 'games', 'kills', 'damage', 'turns']) {
      const r = allow('leaderboard ' + period + ' by ' + cat, await get(B, 'leaderboard/' + period, 'orderBy=' + Q(cat) + '&limitToLast=50'));
      check('leaderboard ' + period + '/' + cat + ' has 4 entries', r.json && Object.keys(r.json).length === 4, r.json);
    }
  }
  const top2 = await get(B, 'leaderboard/all', 'orderBy=' + Q('xp') + '&limitToLast=2');
  check('limitToLast=2 by xp returns the two best (Bob, Alice)',
    top2.json && Object.keys(top2.json).sort().join() === [A.uid, B.uid].sort().join(), top2.json);
  allow('friends board: one entry by path', await get(A, 'leaderboard/' + WEEK + '/' + B.uid));

  // --- friend codes, presence, friends (Social.cs)
  const codeA = 'ABCDEF', codeB = 'BCDEFG';
  allow('read own (missing) code', await get(A, 'players/' + A.uid + '/code'));
  allow('reserve friend code A', await put(A, 'friendCodes/' + codeA, { uid: A.uid }));
  allow('reserve friend code B', await put(B, 'friendCodes/' + codeB, { uid: B.uid }));
  deny('Cleo steals Alice\'s friend code', await put(C, 'friendCodes/' + codeA, { uid: C.uid }));
  deny('friend code pointing at someone else', await put(C, 'friendCodes/CCCCCC', { uid: A.uid }));
  const presA = allow('presence A', await put(A, 'players/' + A.uid, { name: 'Alice', level: 7, code: codeA, seen: SV }));
  check('presence PUT answers seen as a number', presA.json && typeof presA.json.seen === 'number', presA.json);
  allow('presence B', await put(B, 'players/' + B.uid, { name: 'Bob', level: 6, code: codeB, seen: SV }));
  deny('presence with a code you don\'t own', await put(C, 'players/' + C.uid, { name: 'Cleo', level: 1, code: codeA, seen: SV }));
  deny('Bob writes Alice\'s card', await put(B, 'players/' + A.uid, { name: 'Hacked', level: 99, code: codeB, seen: SV }));
  const look = allow('look up friend code', await get(B, 'friendCodes/' + codeA));
  check('friend code resolves to Alice', look.json && look.json.uid === A.uid, look.json);
  allow('read Alice\'s card', await get(B, 'players/' + A.uid));
  allow('Bob adds Alice', await patch(B, 'users/' + B.uid + '/friends/' + A.uid, { name: 'Alice', added: SV }));
  allow('Alice adds Bob', await patch(A, 'users/' + A.uid + '/friends/' + B.uid, { name: 'Bob', added: SV }));
  allow('Alice adds Cleo', await patch(A, 'users/' + A.uid + '/friends/' + C.uid, { name: 'Cleo', added: SV }));
  allow('friend notice to Alice', await put(B, 'inbox/' + A.uid + '/f_' + B.uid, { type: 'friend', from: B.uid, fromName: 'Bob', at: SV }));
  deny('friend notice again while unread', await put(B, 'inbox/' + A.uid + '/f_' + B.uid, { type: 'friend', from: B.uid, fromName: 'Bob', at: SV }));
  deny('Cleo writes Bob\'s friend list', await patch(C, 'users/' + B.uid + '/friends/' + C.uid, { name: 'Cleo', added: SV }));
  const fl = allow('load friends', await get(A, 'users/' + A.uid + '/friends'));
  check('Alice has 2 friends', fl.json && Object.keys(fl.json).length === 2, fl.json);
  allow('remove friend', await del(A, 'users/' + A.uid + '/friends/' + C.uid));
  // friends filter of the leaderboard = own entry + each friend's entry by path
  for (const id of [A.uid, B.uid]) allow('friends leaderboard entry', await get(A, 'leaderboard/' + MONTH + '/' + id));

  // --- gifts + inbox
  const giftId = 'g_' + B.uid + '_' + TODAY;
  allow('Bob gifts Alice', await put(B, 'inbox/' + A.uid + '/' + giftId, { type: 'gift', from: B.uid, fromName: 'Bob', at: SV, item: 'Grenade', amount: 2 }));
  allow('Bob marks the gift day', await patch(B, 'users/' + B.uid + '/friends/' + A.uid, { name: 'Alice', gift: TODAY }));
  deny('second gift the same day', await put(B, 'inbox/' + A.uid + '/' + giftId, { type: 'gift', from: B.uid, fromName: 'Bob', at: SV, item: 'Grenade', amount: 2 }));
  deny('gift under another id', await put(B, 'inbox/' + A.uid + '/g_x_' + TODAY, { type: 'gift', from: B.uid, fromName: 'Bob', at: SV, item: 'Grenade', amount: 2 }));
  deny('gift pretending to be from Cleo', await put(B, 'inbox/' + A.uid + '/g_' + C.uid + '_' + TODAY, { type: 'gift', from: C.uid, fromName: 'Cleo', at: SV, item: 'Grenade', amount: 2 }));
  deny('gift of 500 items', await put(C, 'inbox/' + A.uid + '/g_' + C.uid + '_' + TODAY, { type: 'gift', from: C.uid, fromName: 'Cleo', at: SV, item: 'Grenade', amount: 500 }));
  deny('inbox message dated in the future', await put(C, 'inbox/' + A.uid + '/f_' + C.uid, { type: 'friend', from: C.uid, fromName: 'Cleo', at: Date.now() + 86400000 }));
  deny('Bob reads Alice\'s inbox', await get(B, 'inbox/' + A.uid));
  deny('Bob deletes the gift he sent', await del(B, 'inbox/' + A.uid + '/' + giftId));
  const cnt = allow('inbox count (shallow)', await get(A, 'inbox/' + A.uid, 'shallow=true'));
  check('inbox has 2 messages', cnt.json && Object.keys(cnt.json).length === 2, cnt.json);
  const inbox = allow('load inbox', await get(A, 'inbox/' + A.uid));
  check('gift readable', inbox.json && inbox.json[giftId] && inbox.json[giftId].amount === 2, inbox.json);
  allow('accept gift (delete)', await del(A, 'inbox/' + A.uid + '/' + giftId));
  allow('dismiss notice', await del(A, 'inbox/' + A.uid + '/f_' + B.uid));

  // --- league (League.cs)
  const lg = (u, points, games) => ({ name: u.name, level: 5, points, games, updated: SV });
  allow('league upload A', await put(A, 'league/' + WEEK + '/0/' + A.uid, lg(A, 40, 3)));
  allow('league upload B', await put(B, 'league/' + WEEK + '/0/' + B.uid, lg(B, 12, 1)));
  allow('league last week C', await put(C, 'league/' + LAST_WEEK + '/0/' + C.uid, lg(C, 20, 1)));
  allow('league last week A', await put(A, 'league/' + LAST_WEEK + '/0/' + A.uid, lg(A, 6, 1)));
  deny('league points out of range', await put(A, 'league/' + WEEK + '/0/' + A.uid, lg(A, 5000, 3)));
  deny('league games over 50', await put(A, 'league/' + WEEK + '/0/' + A.uid, lg(A, 40, 51)));
  deny('league tier 7', await put(A, 'league/' + WEEK + '/7/' + A.uid, lg(A, 4, 1)));
  deny('league for somebody else', await put(B, 'league/' + WEEK + '/0/' + A.uid, lg(A, 0, 1)));
  const st = allow('league standings', await get(B, 'league/' + WEEK + '/0', 'orderBy=' + Q('points') + '&limitToLast=200'));
  check('league standings has 2', st.json && Object.keys(st.json).length === 2, st.json);
  allow('league settle reads last week', await get(A, 'league/' + LAST_WEEK + '/0', 'orderBy=' + Q('points') + '&limitToLast=200'));
  allow('promotion: delete this week\'s old tier entry', await del(A, 'league/' + WEEK + '/0/' + A.uid));
  allow('promotion: upload into tier 1', await put(A, 'league/' + WEEK + '/1/' + A.uid, lg(A, 40, 3)));

  // --- host a public match (Host / ReserveCode / WriteLobby)
  const m1 = 'm' + Date.now().toString(16) + 'aaaaaaaa';
  const code1 = 'QWRTY';
  const settings = { levelId: '', matchTime: 300, turnTime: 20, winningScore: 0, powerUps: true, betId: '1NoBet', maxPlayers: 4 };
  const playerRec = u => ({ name: u.name, level: 5, head: '', chest: 'Hat1', feet: '', trophy: '', items: [{ id: 'Grenade', n: 3 }, { id: 'Pistol', n: 9 }], joined: SV, hb: SV });
  allow('code free?', await get(A, 'codes/' + code1));
  allow('reserve join code', await put(A, 'codes/' + code1, { match: m1, host: A.uid, hb: SV }));
  deny('Bob takes Alice\'s join code', await put(B, 'codes/' + code1, { match: 'other', host: B.uid, hb: SV }));
  allow('create match', await put(A, 'matches/' + m1, { host: A.uid, hostName: 'Alice', isPrivate: false, quick: true, code: code1,
    state: 'waiting', created: SV, settings, players: { [A.uid]: playerRec(A) } }));
  deny('create a match for somebody else', await put(B, 'matches/mfake', { host: A.uid, state: 'waiting' }));
  const lob = allow('lobby entry', await put(A, 'lobby/' + m1, { host: A.uid, hostName: 'Alice', level: 7, levelId: '', players: 1, maxPlayers: 4,
    quick: true, code: code1, hb: SV }));
  check('lobby PUT answers hb as a number', lob.json && typeof lob.json.hb === 'number', lob.json);
  deny('Bob writes the lobby entry of Alice\'s match', await put(B, 'lobby/' + m1, { host: B.uid, hostName: 'Bob', players: 1, maxPlayers: 4, hb: SV }));
  deny('Bob lists his own fake lobby entry', await put(B, 'lobby/mfake', { host: B.uid, hostName: 'Bob', players: 1, maxPlayers: 4, hb: SV }));

  // --- quick match: list, join (ListOpenMatches / JoinById)
  const list = allow('list open games', await get(B, 'lobby', 'orderBy=' + Q('hb') + '&limitToLast=30'));
  check('lobby lists the match', list.json && list.json[m1], list.json);
  allow('read match before joining', await get(B, 'matches/' + m1));
  allow('Bob joins', await put(B, 'matches/' + m1 + '/players/' + B.uid, playerRec(B)));
  deny('Bob adds Cleo to the room', await put(B, 'matches/' + m1 + '/players/' + C.uid, playerRec(C)));
  deny('Bob makes himself host', await put(B, 'matches/' + m1 + '/host', B.uid));
  deny('Bob changes the settings', await patch(B, 'matches/' + m1 + '/settings', { turnTime: 60 }));
  deny('Bob starts the match', await patch(B, 'matches/' + m1, { state: 'playing' }));
  allow('host heartbeat (player)', await put(A, 'matches/' + m1 + '/players/' + A.uid + '/hb', SV));
  allow('guest heartbeat', await put(B, 'matches/' + m1 + '/players/' + B.uid + '/hb', SV));
  allow('lobby refresh', await put(A, 'lobby/' + m1, { host: A.uid, hostName: 'Alice', level: 7, levelId: '', players: 2, maxPlayers: 4, quick: true, code: code1, hb: SV }));
  allow('code heartbeat', await put(A, 'codes/' + code1 + '/hb', SV));
  allow('room poll', await get(B, 'matches/' + m1));

  // --- private code join (Join -> codes/{CODE} -> JoinById)
  const cr = allow('look up code', await get(C, 'codes/' + code1));
  check('code points at the match', cr.json && cr.json.match === m1, cr.json);
  allow('Cleo joins by code', await put(C, 'matches/' + m1 + '/players/' + C.uid, playerRec(C)));
  allow('Dan joins', await put(D, 'matches/' + m1 + '/players/' + D.uid, playerRec(D)));
  allow('Dan leaves the waiting room', await del(D, 'matches/' + m1 + '/players/' + D.uid));
  allow('host removes a stale player', await put(D, 'matches/' + m1 + '/players/' + D.uid, playerRec(D)));
  allow('  ...(host delete)', await del(A, 'matches/' + m1 + '/players/' + D.uid));

  // --- start (StartHostedMatch)
  const order = [A.uid, B.uid, C.uid];
  allow('host starts', await patch(A, 'matches/' + m1, { state: 'playing', order, 'settings/seed': 12345, 'settings/levelId': 'Level1',
    turn: { index: 0, player: 0, by: 0, at: SV }, startedAt: SV }));
  allow('host removes lobby entry', await del(A, 'lobby/' + m1));
  allow('host removes code', await del(A, 'codes/' + code1));
  const started = allow('read back', await get(B, 'matches/' + m1));
  check('match is playing with order', started.json && started.json.state === 'playing' && started.json.order.length === 3, started.json && started.json.state);
  deny('Dan joins a started match', await put(D, 'matches/' + m1 + '/players/' + D.uid, playerRec(D)));

  // --- turns (FirebaseBattleNetwork)
  const root = 'matches/' + m1;
  const act = (p, k, extra) => Object.assign({ p, t: 0.25, k }, extra || {});
  allow('A action batch 0', await put(A, root + '/actions/0/a00000', [act(0, 'move', { x: 1.5 }), act(0, 'aim', { x: 0.3, y: 0.7 })]));
  allow('A action batch 1 (fire)', await put(A, root + '/actions/0/a00001', [act(0, 'fire', { px: 3.25, py: -1, s: 'Grenade' })]));
  const turnPoll = allow('B polls turn', await get(B, root + '/turn'));
  check('turn index 0, player 0', turnPoll.json && turnPoll.json.index === 0 && turnPoll.json.player === 0, turnPoll.json);
  const ap = allow('B polls actions', await get(B, root + '/actions/0', 'orderBy=' + Q('$key') + '&startAt=' + Q('a00000')));
  check('B sees 2 batches', ap.json && Object.keys(ap.json).length === 2, ap.json);
  const ap2 = allow('B polls actions from cursor', await get(B, root + '/actions/0', 'orderBy=' + Q('$key') + '&startAt=' + Q('a00001')));
  check('cursor returns 1 batch', ap2.json && Object.keys(ap2.json).length === 1, ap2.json);
  deny('Dan (not in the match) writes actions', await put(D, root + '/actions/0/a00002', [act(0, 'fire')]));
  deny('Dan ends the turn', await patch(D, root, { turn: { index: 1, player: 1, by: 0, at: SV } }));
  const snap = n => ({ from: n, json: JSON.stringify({ nextPlayer: (n + 1) % 3, matchOver: false, terrain: 'x'.repeat(20000) }) });
  // SendTurnEnd as a multi-path PATCH on the match root
  allow('A ends turn 0', await patch(A, root, { 'snapshots/0': snap(0), turn: { index: 1, player: 1, by: 0, at: SV } }));
  allow('B ends turn 1 (guest multi-path PATCH)', await patch(B, root, { 'snapshots/1': snap(1), turn: { index: 2, player: 2, by: 1, at: SV } }));
  allow('C writes actions in turn 2', await put(C, root + '/actions/2/a00000', [act(2, 'move', { x: -1 })]));
  allow('C ends turn 2 and cleans turn 0', await patch(C, root, { 'snapshots/2': snap(2), turn: { index: 3, player: 0, by: 2, at: SV },
    'snapshots/0': null, 'actions/0': null }));
  const s1 = allow('A fetches snapshot 2', await get(A, root + '/snapshots/2'));
  check('snapshot json survives', s1.json && JSON.parse(s1.json.json).nextPlayer === 0, s1.json && s1.json.from);
  const gone = await admin('GET', root + '/snapshots/0');
  check('old snapshot was removed', gone.json === null, gone.json);
  deny('Bob replaces the host in the middle', await patch(B, root, { host: B.uid }));
  allow('players poll', await get(A, root + '/players'));

  // --- chat (SendChat POST + PollChat)
  allow('Bob chats', await post(B, root + '/chat', { p: 1, u: B.uid, at: SV, x: 'nice shot' }));
  allow('Cleo sends a taunt id', await post(C, root + '/chat', { p: 2, u: C.uid, at: SV, k: 'Taunt3' }));
  deny('Dan chats in a match he is not in', await post(D, root + '/chat', { p: 0, u: D.uid, at: SV, x: 'hi' }));
  deny('Bob chats as Cleo', await post(B, root + '/chat', { p: 2, u: C.uid, at: SV, x: 'I quit' }));
  deny('chat line of 500 chars', await post(B, root + '/chat', { p: 1, u: B.uid, at: SV, x: 'y'.repeat(500) }));
  const ch = allow('poll chat (limitToLast)', await get(A, root + '/chat', 'orderBy=' + Q('$key') + '&limitToLast=30'));
  const keys = ch.json ? Object.keys(ch.json).sort() : [];
  check('2 chat lines', keys.length === 2, ch.json);
  const ch2 = allow('poll chat (startAt cursor)', await get(A, root + '/chat', 'orderBy=' + Q('$key') + '&startAt=' + Q(keys[0] || '')));
  check('cursor is inclusive', ch2.json && Object.keys(ch2.json).length === 2, ch2.json);

  // --- end of match: the player who ends it marks it finished, the others leave
  allow('B ends the match (state finished in the PATCH)', await patch(B, root, { 'snapshots/3': snap(3), turn: { index: 4, player: 0, by: 1, at: SV }, state: 'finished' }));
  deny('a player sets the state back to playing', await patch(B, root, { state: 'playing' }));
  for (const u of [A, B, C]) allow('SendLeave ' + u.name, await patch(u, root + '/players/' + u.uid, { left: true, hb: SV }));

  // --- rematch (FirebaseService.Rematch.cs)
  allow('A ready', await put(A, root + '/rematch/' + A.uid, { s: 'ready' }));
  allow('B ready', await put(B, root + '/rematch/' + B.uid, { s: 'ready' }));
  allow('C says no', await put(C, root + '/rematch/' + C.uid, { s: 'left' }));
  deny('Dan votes in a match he was not in', await put(D, root + '/rematch/' + D.uid, { s: 'ready' }));
  deny('Bob votes for Alice', await put(B, root + '/rematch/' + A.uid, { s: 'left' }));
  deny('bad rematch state', await put(B, root + '/rematch/' + B.uid, { s: 'maybe' }));
  allow('poll rematch', await get(B, root + '/rematch'));
  // A hosts the new private match (same as Host() above) and tells the others
  const m2 = 'm' + (Date.now() + 1).toString(16) + 'bbbbbbbb', code2 = 'ZXCVB';
  allow('rematch code', await put(A, 'codes/' + code2, { match: m2, host: A.uid, hb: SV }));
  allow('rematch match', await put(A, 'matches/' + m2, { host: A.uid, hostName: 'Alice', isPrivate: true, quick: false, code: code2,
    state: 'waiting', created: SV, settings, players: { [A.uid]: playerRec(A) } }));
  allow('rematch next', await put(A, root + '/rematch/' + A.uid, { s: 'ready', next: m2 }));
  allow('B joins the rematch', await put(B, 'matches/' + m2 + '/players/' + B.uid, playerRec(B)));

  // --- invite a friend to the private game (Social.SendInvite)
  allow('invite Bob', await put(A, 'inbox/' + B.uid + '/i_' + A.uid + '_' + code2, { type: 'invite', from: A.uid, fromName: 'Alice', at: SV, code: code2, match: m2 }));
  deny('invite again while unread', await put(A, 'inbox/' + B.uid + '/i_' + A.uid + '_' + code2, { type: 'invite', from: A.uid, fromName: 'Alice', at: SV, code: code2, match: m2 }));
  allow('Bob reads his invite', await get(B, 'inbox/' + B.uid));
  allow('Bob dismisses it', await del(B, 'inbox/' + B.uid + '/i_' + A.uid + '_' + code2));

  // --- cleanup
  // a member deletes the finished match (FirebaseBattleNetwork.Close: the lowest slot still there)
  deny('Dan deletes a finished match he was not in', await del(D, root));
  allow('Bob marks it finished again (Close always does)', await patch(B, root, { state: 'finished' }));
  allow('Bob (member, not host) deletes the finished match', await del(B, root));
  deny('marking a deleted match finished recreates nothing', await patch(B, root, { state: 'finished' }));
  // everybody else left mid-match: the last player (not the host) marks it finished and deletes it
  const m4 = 'm' + (Date.now() + 3).toString(16) + 'dddddddd';
  allow('match m4', await put(A, 'matches/' + m4, { host: A.uid, hostName: 'Alice', isPrivate: false, quick: false, code: 'HJKLM',
    state: 'waiting', created: SV, settings, players: { [A.uid]: playerRec(A) } }));
  allow('C joins m4', await put(C, 'matches/' + m4 + '/players/' + C.uid, playerRec(C)));
  allow('m4 starts', await patch(A, 'matches/' + m4, { state: 'playing', order: [A.uid, C.uid], turn: { index: 0, player: 0, by: 0, at: SV }, startedAt: SV }));
  allow('host leaves m4', await patch(A, 'matches/' + m4 + '/players/' + A.uid, { left: true, hb: SV }));
  deny('C deletes m4 while it is playing', await del(C, 'matches/' + m4));
  allow('C marks m4 finished', await patch(C, 'matches/' + m4, { state: 'finished' }));
  allow('C deletes m4', await del(C, 'matches/' + m4));
  // host cancels the rematch room (CancelMatchmaking)
  allow('host cancels the room', await patch(A, 'matches/' + m2, { state: 'cancelled' }));
  allow('guest leaves a cancelled room', await del(B, 'matches/' + m2 + '/players/' + B.uid));
  allow('host deletes the room', await del(A, 'matches/' + m2));
  allow('host deletes its code', await del(A, 'codes/' + code2));

  // CleanupPreviousMatch: read code of a left-over match, delete lobby + code + match
  const m3 = 'm' + (Date.now() + 2).toString(16) + 'cccccccc';
  allow('left-over match', await put(A, 'matches/' + m3, { host: A.uid, hostName: 'Alice', isPrivate: false, quick: false, code: 'PLMNB',
    state: 'waiting', created: SV, settings, players: { [A.uid]: playerRec(A) } }));
  allow('left-over lobby', await put(A, 'lobby/' + m3, { host: A.uid, hostName: 'Alice', level: 7, levelId: '', players: 1, maxPlayers: 4, quick: false, code: 'PLMNB', hb: SV }));
  allow('read left-over code', await get(A, 'matches/' + m3 + '/code'));
  deny('Bob deletes a fresh lobby entry', await del(B, 'lobby/' + m3));
  // stale lobby entries may be removed by anybody (ListOpenMatches)
  await admin('PATCH', 'lobby/' + m3, { hb: Date.now() - 200000 });
  allow('Bob deletes a lobby entry stale for 3 minutes', await del(B, 'lobby/' + m3));
  deny('Bob deletes a fresh match', await del(B, 'matches/' + m3));

  // sweep of abandoned matches (FirebaseService.SweepAbandoned): any signed-in player may delete matches
  // created more than 3 hours ago; listing is only allowed as that query (orderBy created, endAt, limitToFirst <= 3)
  await admin('PATCH', 'matches/' + m3, { created: Date.now() - 4 * 3600000 });
  const cutoff = Date.now() - 3 * 3600000;
  deny('list all matches', await get(B, 'matches'));
  deny('list matches by created without endAt', await get(B, 'matches', 'orderBy=' + Q('created') + '&limitToFirst=3'));
  deny('sweep query asking for 50', await get(B, 'matches', 'orderBy=' + Q('created') + '&endAt=' + cutoff + '&limitToFirst=50'));
  const sw = allow('sweep query', await get(B, 'matches', 'orderBy=' + Q('created') + '&endAt=' + cutoff + '&limitToFirst=3'));
  check('sweep finds the abandoned match', sw.json && sw.json[m3], sw.json && Object.keys(sw.json));
  allow('Bob deletes the abandoned match', await del(B, 'matches/' + m3));
  allow('host-side codes cleanup', await del(A, 'codes/PLMNB'));

  // --- final state: nothing of the matches is left
  const left = await admin('GET', 'matches', undefined, 'shallow=true');
  check('no matches left', left.json === null, left.json);

  // --- an expired token is refused with 401 and a message mentioning the token (DbRoutine retries after refresh)
  const expired = await db({ idToken: 'eyJhbGciOiJub25lIn0.eyJleHAiOjEsInN1YiI6IngifQ.' }, 'GET', 'users/x');
  check('bad token -> 401', expired.code === 401, expired.code + ' ' + expired.text);
}

main().then(() => {
  console.log('\n' + passed + ' passed, ' + failures.length + ' failed');
  for (const f of failures) console.log('  ' + f);
  process.exit(failures.length ? 1 : 0);
}).catch(e => { console.error(e); process.exit(2); });
