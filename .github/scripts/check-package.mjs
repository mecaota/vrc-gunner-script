// パッケージの形を、Unity を使わずに確かめる(gunner の Verify から呼ぶ)。
//   1. package.json: 必須の項目、版の形(SemVer)、依存は com.vrchat.worlds だけ
//   2. CHANGELOG.md に、package.json の版の節があり、その節に日付の仮置き YYYY-MM-DD が残っていない
//   3. Unity に取り込まれるファイルとフォルダに .meta があり、相手の無い .meta が無い
//   4. .meta の GUID が重複していない
// 使い方: node .github/scripts/check-package.mjs <パッケージの根>
// 終了コード: 問題が無ければ 0、あれば 1。

import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.argv[2] ?? '.');
const errors = [];
const fail = (msg) => errors.push(msg);

// ---- 1. package.json ----
const pkg = JSON.parse(readFileSync(path.join(root, 'package.json'), 'utf8').replace(/^﻿/, ''));
for (const key of ['name', 'displayName', 'version', 'unity', 'description', 'license', 'author']) {
  if (!pkg[key]) fail(`package.json に ${key} がありません`);
}
if (pkg.name !== 'com.mecaota.gunner') fail(`package.json の name が com.mecaota.gunner ではありません: ${pkg.name}`);
const num = '(0|[1-9]\\d*)';
if (!new RegExp(`^${num}\\.${num}\\.${num}(-[0-9A-Za-z.-]+)?$`).test(pkg.version ?? '')) {
  fail(`version が SemVer の形ではありません: ${pkg.version}`);
}
if (pkg.license !== 'MIT') fail(`license は MIT のはずです: ${pkg.license}`);
const deps = Object.keys(pkg.vpmDependencies ?? {});
if (deps.length !== 1 || deps[0] !== 'com.vrchat.worlds') {
  fail(`vpmDependencies は com.vrchat.worlds だけのはずです: ${deps.join(', ') || '(なし)'}`);
}
if (pkg.dependencies && Object.keys(pkg.dependencies).length) {
  fail('dependencies(UPM の依存)は使いません。vpmDependencies に書きます');
}

// ---- 2. CHANGELOG ----
// version が無い(または文字列でない)ときは、上で「項目不足」として数えてあるので、節の確認は飛ばす
const changelog = path.join(root, 'CHANGELOG.md');
if (!existsSync(changelog)) fail('CHANGELOG.md がありません');
else if (typeof pkg.version === 'string' && pkg.version) {
  const text = readFileSync(changelog, 'utf8').replace(/\r\n/g, '\n');
  const head = new RegExp(`^##\\s*\\[?${pkg.version.replace(/\./g, '\\.')}\\]?(?![0-9A-Za-z.-]).*$`, 'm').exec(text);
  if (!head) {
    fail(`CHANGELOG.md に ${pkg.version} の節がありません`);
  } else {
    // 現在版の節(見出しから次の "## " の手前まで)に、日付の仮置き YYYY-MM-DD が残っていたら失敗にする
    const rest = text.slice(head.index + head[0].length);
    const next = /^##\s/m.exec(rest);
    const section = head[0] + (next ? rest.slice(0, next.index) : rest);
    if (section.includes('YYYY-MM-DD')) {
      fail(`CHANGELOG.md の ${pkg.version} の節に、日付の仮置き YYYY-MM-DD が残っています`);
    }
  }
}

// ---- 3, 4. .meta ----
// Unity が無視する物: 名前が "." で始まる、"~" で終わる、cvs、.tmp(Unity の規則)
const ignored = (name) => name.startsWith('.') || name.endsWith('~') || name.toLowerCase() === 'cvs' || name.endsWith('.tmp');
const guids = new Map();
function walk(dir) {
  const names = readdirSync(dir);
  const set = new Set(names);
  for (const name of names) {
    if (ignored(name)) continue;
    const full = path.join(dir, name);
    const r = path.relative(root, full).split(path.sep).join('/');
    if (name.endsWith('.meta')) {
      const target = name.slice(0, -5);
      if (!set.has(target)) fail(`相手の無い .meta: ${r}`);
      const m = /^guid:\s*([0-9a-f]{32})/m.exec(readFileSync(full, 'utf8'));
      if (!m) fail(`GUID の無い .meta: ${r}`);
      else if (guids.has(m[1])) fail(`GUID の重複: ${r} と ${guids.get(m[1])}`);
      else guids.set(m[1], r);
      continue;
    }
    if (!set.has(`${name}.meta`)) fail(`.meta が無い: ${r}`);
    if (statSync(full).isDirectory()) walk(full);
  }
}
walk(root);

for (const e of errors) {
  console.log(e);
  if (process.env.GITHUB_ACTIONS) console.log(`::error::${e}`);
}
console.log(errors.length === 0 ? `問題なし(${pkg.name} ${pkg.version}、.meta ${guids.size} 個)` : `${errors.length} 件の問題`);
process.exit(errors.length === 0 ? 0 : 1);
