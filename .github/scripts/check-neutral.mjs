// 公開リポジトリ(gunner)に、持ち込んではいけない物が無いかを調べる。
//
// 調べること:
//   1. 禁止語。語の一覧はこのファイルにもリポジトリにも書かない(書くと一覧そのものが公開される)。
//      環境変数 NEUTRAL_WORDS(改行区切り)か、--words <ファイル> で渡す。
//      CI ではリポジトリの secret NEUTRAL_WORDS から渡す。
//      1行1語。大文字と小文字は区別しない。"re:" で始まる行は正規表現として読む。"#" で始まる行は注釈。
//   2. 音声ファイル(.wav .mp3 .ogg など)。サンプルにも入れない。
//   3. パッケージの外を指す GUID(プレハブ・シーン・マテリアルなどの参照)。
//      Unity の組み込み以外は、allowed-guids.txt に載っている物(VRChat SDK など)だけを許す。
//      --project <Unity プロジェクト> を付けると、外の GUID がどのファイルを指すかを表示する(手元で使う)。
//      --write-allowed を付けると、Packages/com.vrchat.* を指す GUID を allowed-guids.txt に書き足す。
//   4. 手元のパス(ドライブ文字から始まる絶対パス、ユーザーのホーム)。
//   5. --history を付けると、git の全履歴の追加行とファイル名にも 1 と 2 を当てる。
//
// 使い方:  node .github/scripts/check-neutral.mjs <パッケージの根> [--history] [--words file]
//                 [--project <Unity プロジェクトの根>] [--write-allowed]
// 終了コード: 問題が無ければ 0、あれば 1、使い方の誤りは 2。

import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync, appendFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const args = process.argv.slice(2);
const root = path.resolve(args.find((a) => !a.startsWith('--') && !isOptionValue(a)) ?? '.');
const opt = (name) => {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : undefined;
};
const withHistory = args.includes('--history');
const writeAllowed = args.includes('--write-allowed');
const projectRoot = opt('--project');
const self = fileURLToPath(import.meta.url);
const allowedFile = path.join(path.dirname(self), 'allowed-guids.txt');

const AUDIO = /\.(wav|mp3|ogg|oga|aif|aiff|flac|m4a|aac|wma|opus|mod|it|s3m|xm)$/i;
const YAML_ASSET = /\.(prefab|unity|mat|asset|controller|overrideController|anim|playable|mask|physicMaterial|physicsMaterial2D|lighting|signal|spriteatlas|flare|guiskin|renderTexture|cubemap|terrainlayer)$/i;
const TEXT = /\.(cs|shader|cginc|hlsl|compute|json|md|txt|tsv|csv|yml|yaml|mjs|js|asmdef|asmref|uss|uxml|xml|html|css|meta)$/i;
// Unity 組み込みのリソース(Default-Material など)
const BUILTIN_GUIDS = new Set([
  '0000000000000000e000000000000000',
  '0000000000000000f000000000000000',
  '0000000000000000d000000000000000',
]);
const LOCAL_PATH = /(?:\b[A-Za-z]:[\\/](?:Users|VRChatProjects|Program Files)|\/home\/[a-z]|\/Users\/[A-Za-z])/;

function isOptionValue(a) {
  const i = args.indexOf(a);
  return i > 0 && ['--words', '--project'].includes(args[i - 1]);
}

const problems = [];
const report = (kind, where, detail) => problems.push({ kind, where, detail });

// ---- 禁止語の読み込み ----
function loadWords() {
  let text = process.env.NEUTRAL_WORDS ?? '';
  const file = opt('--words');
  if (file) text += '\n' + readFileSync(file, 'utf8');
  const rules = [];
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    if (line.startsWith('re:')) rules.push({ label: line, re: new RegExp(line.slice(3), 'i') });
    else rules.push({ label: line, re: new RegExp(line.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i') });
  }
  return rules;
}
const words = loadWords();
if (words.length === 0) {
  console.error('禁止語の一覧がありません。NEUTRAL_WORDS か --words で渡してください。');
  process.exit(2);
}

// ---- ファイルの一覧(.git と node_modules は除く。このスクリプト自身は語の検査から外す) ----
function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    if (name === '.git' || name === 'node_modules' || name === '_site') continue;
    const full = path.join(dir, name);
    if (statSync(full).isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}
const files = walk(root);
const rel = (f) => path.relative(root, f).split(path.sep).join('/');

// ---- 1, 2, 4: 中身と名前 ----
for (const f of files) {
  const r = rel(f);
  if (AUDIO.test(r)) report('音声ファイル', r, '公開側には音声ファイルを入れない');
  for (const w of words) if (w.re.test(r)) report('禁止語(ファイル名)', r, w.label);
  if (path.resolve(f) === path.resolve(self)) continue;
  if (!TEXT.test(r) && !YAML_ASSET.test(r)) continue;
  const lines = readFileSync(f, 'utf8').split(/\r?\n/);
  lines.forEach((line, i) => {
    for (const w of words) if (w.re.test(line)) report('禁止語', `${r}:${i + 1}`, w.label);
    if (LOCAL_PATH.test(line)) report('手元のパス', `${r}:${i + 1}`, line.trim().slice(0, 120));
  });
}

// ---- 3: パッケージの外を指す GUID ----
const ownGuids = new Set();
for (const f of files.filter((f) => f.endsWith('.meta'))) {
  const m = /^guid:\s*([0-9a-f]{32})/m.exec(readFileSync(f, 'utf8'));
  if (m) ownGuids.add(m[1]);
}
const allowed = new Set(existsSync(allowedFile)
  ? readFileSync(allowedFile, 'utf8').split(/\r?\n/).map((l) => l.split('#')[0].trim()).filter(Boolean)
  : []);
const external = new Map(); // guid -> 参照している場所
for (const f of files.filter((f) => YAML_ASSET.test(f))) {
  const text = readFileSync(f, 'utf8');
  for (const line of text.split(/\r?\n/)) {
    // U# が各プロジェクトで作り直す、コンパイル済みプログラムへの参照は数えない
    // (Assets/SerializedUdonPrograms/ を指す。購入者・利用者の環境では作り直される)
    if (/^\s*serialized(Udon)?ProgramAsset:/.test(line)) continue;
    const m = /guid:\s*([0-9a-f]{32})/.exec(line);
    if (!m) continue;
    const g = m[1];
    if (ownGuids.has(g) || BUILTIN_GUIDS.has(g)) continue;
    if (!external.has(g)) external.set(g, new Set());
    external.get(g).add(rel(f));
  }
}
const guidPaths = projectRoot ? indexProjectGuids(path.resolve(projectRoot)) : new Map();
const newlyAllowed = [];
for (const [g, where] of external) {
  if (allowed.has(g)) continue;
  const target = guidPaths.get(g);
  if (writeAllowed && target && /^Packages\/com\.vrchat\./.test(target)) {
    newlyAllowed.push(`${g}  # ${target}`);
    continue;
  }
  report('外の参照', [...where].join(', '), `${g}${target ? ` -> ${target}` : '(参照先は --project で確認)'}`);
}
if (newlyAllowed.length) {
  appendFileSync(allowedFile, newlyAllowed.join('\n') + '\n');
  console.log(`allowed-guids.txt に ${newlyAllowed.length} 件を足しました。差分を目で確認してから commit してください。`);
}

function indexProjectGuids(project) {
  const map = new Map();
  for (const top of ['Assets', 'Packages']) {
    const dir = path.join(project, top);
    if (!existsSync(dir)) continue;
    for (const f of walk(dir)) {
      if (!f.endsWith('.meta')) continue;
      const m = /^guid:\s*([0-9a-f]{32})/m.exec(readFileSync(f, 'utf8'));
      if (m) map.set(m[1], path.relative(project, f).split(path.sep).join('/').replace(/\.meta$/, ''));
    }
  }
  return map;
}

// ---- 5: 履歴 ----
if (withHistory) {
  const git = (...a) => execFileSync('git', ['-C', root, ...a], { encoding: 'utf8', maxBuffer: 1 << 30 });
  let commit = '';
  let file = '';
  for (const line of git('log', '--all', '-p', '--no-color', '--format=@@commit %H').split('\n')) {
    if (line.startsWith('@@commit ')) { commit = line.slice(9, 17); continue; }
    if (line.startsWith('+++ b/')) {
      file = line.slice(6);
      if (AUDIO.test(file)) report('音声ファイル(履歴)', `${commit} ${file}`, '履歴に残っている');
      for (const w of words) if (w.re.test(file)) report('禁止語(履歴のファイル名)', `${commit} ${file}`, w.label);
      continue;
    }
    if (!line.startsWith('+') || line.startsWith('+++')) continue;
    if (/check-neutral\.mjs$/.test(file)) continue;
    for (const w of words) if (w.re.test(line)) report('禁止語(履歴)', `${commit} ${file}`, w.label);
  }
  // バイナリの追加(diff に中身が出ない物)もファイル名で見る
  for (const name of git('log', '--all', '--name-only', '--format=').split('\n')) {
    if (name && AUDIO.test(name)) report('音声ファイル(履歴)', name, '履歴に残っている');
  }
}

// ---- 結果 ----
const seen = new Set();
const unique = problems.filter((p) => {
  const k = `${p.kind}|${p.where}|${p.detail}`;
  return seen.has(k) ? false : (seen.add(k), true);
});
for (const p of unique) {
  // 禁止語の中身は CI のログに出さない(公開リポジトリのログは誰でも読める)
  const detail = p.kind.startsWith('禁止語') && process.env.CI ? '(語は伏せる)' : p.detail;
  console.log(`${p.kind}\t${p.where}\t${detail}`);
  if (process.env.GITHUB_ACTIONS) console.log(`::error::${p.kind}: ${p.where}`);
}
console.log(unique.length === 0 ? '問題なし' : `${unique.length} 件の問題`);
process.exit(unique.length === 0 ? 0 : 1);
