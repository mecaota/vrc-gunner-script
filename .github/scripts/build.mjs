// このリポジトリの GitHub Releases と根の README.md から、VPM の listing(index.json)と
// 配布ページ(index.html)を作る。
//
// 環境変数:
//   GITHUB_REPOSITORY  リポジトリの owner/name(GitHub Actions では自動で入る)
//   GITHUB_TOKEN       GitHub API に使うトークン(任意。回数の上限が上がる)
//   PACKAGE_NAME       配布ページに出す VPM パッケージの id(例 com.mecaota.gunner)
//   LISTING_URL        index.json の公開 URL(必須。例 https://github.pito.run/vrc-gunner-scripts/index.json。
//                      github.io の URL は http に飛ぶので使えない)
//   SITE_DIR           出力先。リポジトリの根からの相対(既定 _site)
//   DEFAULT_BRANCH     README.md の相対リンクを解決するブランチ(既定 main)
//
// 複製元(同じ作者のシェーダーのリポジトリの build.mjs)からの変更:
//   1. 配布ページの雛形を .github/website/ から読む(根に置くと Unity が取り込み、zip にも入るため)
//   2. package.json をリポジトリの根から読む(リポジトリの根 = パッケージの根)
//   3. API の User-Agent を gunner-site-build にした
//   4. 根の package.json の name が PACKAGE_NAME と違うときは、その package.json を使わない

import { createHash } from 'node:crypto';
import { cpSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { unzipSync } from 'fflate';
import { marked } from 'marked';
import { gfmHeadingId } from 'marked-gfm-heading-id';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const repo = requireEnv('GITHUB_REPOSITORY');
const [owner] = repo.split('/');
const packageName = process.env.PACKAGE_NAME || '';
const listingUrl = requireEnv('LISTING_URL');
const siteDir = path.resolve(root, process.env.SITE_DIR || '_site');
const defaultBranch = process.env.DEFAULT_BRANCH || 'main';
const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN || '';

const websiteDir = path.join(root, '.github', 'website');
const readmePath = path.join(root, 'README.md');
const repoUrl = `https://github.com/${repo}`;

main().catch((error) => {
  console.error(error);
  process.exit(1);
});

async function main() {
  console.log(`Repository: ${repo}`);
  console.log(`Listing URL: ${listingUrl}`);

  const releases = await fetchReleases();
  console.log(`Found ${releases.length} release(s)`);

  const manifests = await collectManifests(releases);
  if (manifests.length === 0) {
    console.warn('No package zip with a package.json was found in the releases.');
  }

  const primary = pickPrimaryManifest(manifests);
  const listing = buildListing(manifests, primary);

  mkdirSync(siteDir, { recursive: true });
  cpSync(websiteDir, siteDir, { recursive: true });
  writeFileSync(path.join(siteDir, 'index.json'), `${JSON.stringify(listing, null, 2)}\n`);
  writeFileSync(path.join(siteDir, 'index.html'), renderIndexHtml(primary));

  for (const manifest of manifests) {
    console.log(`  ${manifest.name}@${manifest.version}  ${manifest.url}`);
  }
  console.log(`Wrote ${path.relative(root, siteDir)}/index.json and index.html`);
}

function requireEnv(name) {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Environment variable ${name} is required`);
  }
  return value;
}

async function fetchReleases() {
  const headers = {
    Accept: 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28',
    'User-Agent': 'gunner-site-build',
  };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const releases = [];
  for (let page = 1; ; page += 1) {
    const url = `https://api.github.com/repos/${repo}/releases?per_page=100&page=${page}`;
    const response = await fetch(url, { headers });
    if (!response.ok) {
      throw new Error(`GitHub API request failed: ${response.status} ${response.statusText} (${url})`);
    }
    const batch = await response.json();
    releases.push(...batch);
    if (batch.length < 100) {
      break;
    }
  }
  return releases.filter((release) => !release.draft);
}

async function collectManifests(releases) {
  const manifests = [];
  for (const release of releases) {
    for (const asset of release.assets ?? []) {
      if (!asset.name.endsWith('.zip')) {
        continue;
      }
      const manifest = await readManifestFromZip(asset.browser_download_url);
      if (!manifest) {
        console.warn(`Skipping ${asset.name} from release ${release.tag_name}: no valid package.json inside`);
        continue;
      }
      manifests.push(manifest);
    }
  }
  return manifests;
}

async function readManifestFromZip(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Could not download ${url}: ${response.status} ${response.statusText}`);
  }
  const bytes = new Uint8Array(await response.arrayBuffer());

  // 壊れた添付ファイル1つで listing 全体を止めないよう、zip と JSON の誤りはその添付だけを飛ばす
  let manifest;
  try {
    const entries = unzipSync(bytes, { filter: (file) => file.name === 'package.json' });
    const entry = entries['package.json'];
    if (!entry) {
      return null;
    }
    manifest = JSON.parse(new TextDecoder('utf-8').decode(entry));
  } catch (error) {
    console.warn(`Could not read package.json from ${url}: ${error.message}`);
    return null;
  }
  if (typeof manifest.name !== 'string' || typeof manifest.version !== 'string') {
    return null;
  }
  manifest.url = url;
  manifest.zipSHA256 = createHash('sha256').update(bytes).digest('hex');
  return manifest;
}

// 配布ページに出すパッケージの情報。名前・説明・ライセンス・作者は、同じブランチの README と
// 合うようにリポジトリの package.json に従い、版だけは最新の Release のものを使う
function pickPrimaryManifest(manifests) {
  const latest = pickLatestRelease(manifests);
  const local = readLocalManifest();
  if (!latest && !local) {
    throw new Error('Could not determine the package to show. Set PACKAGE_NAME or publish a release.');
  }
  if (!latest) {
    console.warn(`No release found for ${packageName}; using the repository package.json for the landing page`);
    return local;
  }
  return local ? { ...latest, ...local, version: latest.version } : latest;
}

function pickLatestRelease(manifests) {
  let candidates = packageName ? manifests.filter((m) => m.name === packageName) : manifests;
  const stable = candidates.filter((m) => !parseVersion(m.version)?.prerelease);
  if (stable.length > 0) {
    candidates = stable;
  }
  if (candidates.length === 0) {
    return null;
  }
  return candidates.slice().sort((a, b) => compareVersions(b.version, a.version))[0];
}

// リポジトリの根がパッケージの根なので、package.json は根にある。
// 名前が PACKAGE_NAME と違うときは使わない(別のパッケージの情報を出さないため)
function readLocalManifest() {
  if (!packageName) {
    return null;
  }
  const localPath = path.join(root, 'package.json');
  if (!existsSync(localPath)) {
    return null;
  }
  const manifest = JSON.parse(stripBom(readFileSync(localPath, 'utf8')));
  return manifest.name === packageName ? manifest : null;
}

// Unity や Windows のエディタは、UTF-8 に BOM を付けて保存しがち
function stripBom(text) {
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

function buildListing(manifests, primary) {
  const packages = {};
  for (const manifest of manifests) {
    packages[manifest.name] ??= { versions: {} };
    packages[manifest.name].versions[manifest.version] = manifest;
  }
  return {
    name: `${primary.displayName || primary.name} Listing`,
    id: `${primary.name}.listing`,
    author: primary.author?.name || owner,
    url: listingUrl,
    packages,
  };
}

function renderIndexHtml(primary) {
  const template = readFileSync(path.join(websiteDir, 'index.html'), 'utf8');
  const values = {
    DISPLAY_NAME: primary.displayName || primary.name,
    DESCRIPTION: primary.description || '',
    VERSION: primary.version || '',
    PACKAGE_NAME: primary.name,
    LICENSE: primary.license || '',
    AUTHOR_NAME: primary.author?.name || owner,
    AUTHOR_URL: primary.author?.url || `https://github.com/${owner}`,
    LISTING_URL: listingUrl,
    VCC_URL: `vcc://vpm/addRepo?url=${encodeURIComponent(listingUrl)}`,
    REPO_URL: repoUrl,
    RELEASES_URL: `${repoUrl}/releases`,
  };

  return template.replace(/\{\{\s*([A-Z_]+)\s*\}\}/g, (match, key) => {
    if (key === 'README_HTML') {
      return renderReadme();
    }
    if (!(key in values)) {
      throw new Error(`Unknown placeholder ${match} in .github/website/index.html`);
    }
    return escapeHtml(values[key]);
  });
}

function renderReadme() {
  if (!existsSync(readmePath)) {
    return '';
  }
  let markdown = stripBom(readFileSync(readmePath, 'utf8'));
  // ページの見出しにパッケージ名がもう出ているので、先頭の H1 は落とす
  markdown = markdown.replace(/^\s*#\s[^\n]*\n/, '');

  marked.use(gfmHeadingId(), {
    gfm: true,
    walkTokens(token) {
      if (token.type === 'link' || token.type === 'image') {
        token.href = absolutizeLink(token.href, token.type === 'image');
      } else if (token.type === 'html') {
        // <img src="img/x.png" width="600"> のような生の HTML は marked がそのまま通す
        token.text = absolutizeHtmlUrls(token.text);
      }
    },
  });
  return marked.parse(markdown);
}

// README.md の相対リンクはリポジトリのファイルを指すので、配布ページからは GitHub の URL に直す
function absolutizeLink(href, isImage) {
  if (!href || /^(?:[a-z][a-z0-9+.-]*:|\/\/|#)/i.test(href)) {
    return href;
  }
  const filePath = href.replace(/^(?:\.\/|\/)+/, '');
  return isImage
    ? `https://raw.githubusercontent.com/${repo}/${defaultBranch}/${filePath}`
    : `${repoUrl}/blob/${defaultBranch}/${filePath}`;
}

// 生の HTML の src と href も、Markdown のリンクや画像と同じように直す
function absolutizeHtmlUrls(html) {
  return html.replace(/\b(src|href)=("|')([^"']*)\2/gi, (match, attr, quote, value) =>
    `${attr}=${quote}${absolutizeLink(value, attr.toLowerCase() === 'src')}${quote}`);
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function parseVersion(version) {
  const match = /^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?/.exec(version ?? '');
  if (!match) {
    return null;
  }
  return {
    main: [Number(match[1]), Number(match[2]), Number(match[3])],
    prerelease: match[4] ? match[4].split('.') : null,
  };
}

function compareVersions(a, b) {
  const pa = parseVersion(a);
  const pb = parseVersion(b);
  if (!pa || !pb) {
    return String(a).localeCompare(String(b));
  }
  for (let i = 0; i < 3; i += 1) {
    if (pa.main[i] !== pb.main[i]) {
      return pa.main[i] - pb.main[i];
    }
  }
  if (!pa.prerelease && !pb.prerelease) return 0;
  if (!pa.prerelease) return 1;
  if (!pb.prerelease) return -1;
  const length = Math.max(pa.prerelease.length, pb.prerelease.length);
  for (let i = 0; i < length; i += 1) {
    const x = pa.prerelease[i];
    const y = pb.prerelease[i];
    if (x === undefined) return -1;
    if (y === undefined) return 1;
    const xNumeric = /^\d+$/.test(x);
    const yNumeric = /^\d+$/.test(y);
    if (xNumeric && yNumeric) {
      if (Number(x) !== Number(y)) return Number(x) - Number(y);
    } else if (xNumeric) {
      return -1;
    } else if (yNumeric) {
      return 1;
    } else if (x !== y) {
      return x < y ? -1 : 1;
    }
  }
  return 0;
}
