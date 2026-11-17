#!/bin/bash
# このパッケージの確かめ(Unity を開かずに、Git Bash で動かす)。
#   1. csc で Runtime・Editor・Tests/Editor をコンパイルする(警告0・エラー0)。.cs は find で集めるので、ファイルを足しても
#      この台本は書き直さなくてよい。参照と定義は、Unity が作った rsp(Library/Bee/artifacts/*.dag/<アセンブリ>.rsp)から取る。
#   2. 純計算のテスト(Tests/Editor/Models と Tools~/verify/pure の *.cs)を、ファイルごとに pwsh で動かす(run-pure.ps1)。
#      Tests/Editor の NUnit のテストは Unity の Test Runner(EditMode)で動かす。
#   3. Editor/strings.tsv の重複キーを調べる。
#
# 使い方: bash "Tools~/verify/build.sh"
#   UNITY_PROJECT       このパッケージを入れて一度コンパイルした Unity プロジェクトの根。既定はこのパッケージの2つ上
#                       (Packages/ の下に置いた時)。作業用の写しで動かす時は指定する
#   OUT                 dll を置くフォルダ(既定: 一時フォルダ)。ほかのパッケージの build.sh がここの dll を参照に使う
#   SKIP_PURE=1         2 を飛ばす
# 終了コード: 全部通れば 0、どれか落ちれば 1、使い方の誤りは 2。
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
PROJECT=${UNITY_PROJECT:-$(cd "$ROOT/../.." && pwd)}
PROJECT=$(cd "$PROJECT" 2>/dev/null && pwd) || { echo "UNITY_PROJECT not found"; exit 2; }
OUT=${OUT:-$(mktemp -d)}
mkdir -p "$OUT"
FAIL=0

# Editor のアセンブリの rsp がある .dag(Unity エディタのコンパイル)を選ぶ
DAG=""
for d in $(ls -td "$PROJECT"/Library/Bee/artifacts/*.dag 2>/dev/null); do
    if [ -f "$d/Gunner.Editor.rsp" ] && [ -f "$d/Gunner.rsp" ]; then DAG="$d"; break; fi
done
[ -n "$DAG" ] || { echo "no Library/Bee/artifacts/*.dag/Gunner.Editor.rsp under $PROJECT (open and compile the project in Unity once)"; exit 2; }
DAGREL=${DAG#$PROJECT/}
# Unity の Editor/Data(参照の rsp の行から引く)
UDATA=$(grep -m1 -o '[^"]*/UnityReferenceAssemblies/' "$DAG/Gunner.rsp" | sed 's#/UnityReferenceAssemblies/$##')
[ -n "$UDATA" ] || { echo "cannot find the Unity editor path in $DAG/Gunner.rsp"; exit 2; }
M=$(cygpath -m "$OUT")

csc() { ( cd "$PROJECT" && "$UDATA/NetCoreRuntime/dotnet.exe" "$UDATA/DotNetSdkRoslyn/csc.dll" -nologo -noconfig "$@" ); }

# rsp から .cs と出力の行を除き、自前で作る dll への参照を差し替える。$1 = 元の rsp、$2.. = 「名前=dll」
# -nullable も除く(プロジェクトの設定で付くことがあり、パッケージのコードは nullable の注釈を使っていないため)
base_rsp() {
    local src=$1; shift
    local expr=()
    for pair in "$@"; do
        local name=${pair%%=*} dll=${pair#*=}
        expr+=(-e "s#^-r:\"$DAGREL/$name.ref.dll\"#-r:\"$dll\"#")
    done
    grep -v -E '\.cs"?$|^-out:|^-refout:|^/additionalfile:|^-nullable' "$src" | sed "${expr[@]:--e s/^//}"
}

files() { [ -d "$ROOT/$1" ] && find "$ROOT/$1" -name '*.cs' | sort | while read -r f; do echo "\"$(cygpath -m "$f")\""; done; }

# $1 = 名前、$2 = rsp、残り = csc に足す引数。警告・エラーの数を数える
compile() {
    local name=$1 rsp=$2; shift 2
    local count
    count=$(grep -c '\.cs"$' "$rsp")
    local log="$OUT/$name.log"
    csc -target:library -warn:4 -nowarn:1701,1702 -out:"$M/$name.dll" @"$(cygpath -m "$rsp")" "$@" > "$log" 2>&1
    local code=$?
    local warn err
    warn=$(grep -c 'warning CS' "$log")
    err=$(grep -c 'error CS' "$log")
    echo "$name: $count files, warnings $warn, errors $err (exit $code)"
    if [ "$code" -ne 0 ] || [ "$warn" -ne 0 ] || [ "$err" -ne 0 ]; then
        grep -E 'warning CS|error CS' "$log" | head -40
        FAIL=1
    fi
}

echo "project: $PROJECT"
echo "dag:     $DAGREL"
echo "out:     $OUT"

# ---------------- 1. コンパイル ----------------
{ base_rsp "$DAG/Gunner.rsp"; files Runtime; } > "$OUT/rt.rsp"
compile Gunner "$OUT/rt.rsp"
{ base_rsp "$DAG/Gunner.Editor.rsp" "Gunner=$M/Gunner.dll"; files Editor; } > "$OUT/ed.rsp"
compile Gunner.Editor "$OUT/ed.rsp"
if [ -d "$ROOT/Tests/Editor" ]; then
    # テストは Editor の rsp(nunit と TestRunner の参照、UNITY_INCLUDE_TESTS を含む)を土台にする
    { base_rsp "$DAG/Gunner.Editor.rsp" "Gunner=$M/Gunner.dll"; echo "-r:\"$M/Gunner.Editor.dll\""; files Tests/Editor; } > "$OUT/tests.rsp"
    compile Gunner.Tests.Editor "$OUT/tests.rsp"
fi

# ---------------- 2. 純計算のテスト ----------------
if [ "${SKIP_PURE:-0}" != "1" ]; then
    if command -v pwsh > /dev/null; then
        for f in $(find "$ROOT/Tests/Editor/Models" "$ROOT/Tools~/verify/pure" -name '*.cs' 2>/dev/null | sort); do
            # 身代わりの型だけのファイル(Run を持たない)は、使う側のテストが verify-uses で読む
            grep -q 'static string Run(' "$f" || continue
            pwsh -NoProfile -File "$(cygpath -w "$HERE/run-pure.ps1")" -File "$(cygpath -w "$f")" -Root "$(cygpath -w "$ROOT")" ${GUNNER_ROOT:+-Gunner "$(cygpath -w "$GUNNER_ROOT")"} || FAIL=1
        done
    else
        echo "pure tests: skipped (pwsh not found)"
    fi
fi

# ---------------- 3. 文字列表 ----------------
if [ -f "$ROOT/Editor/strings.tsv" ]; then
    dup=$(cut -f1 "$ROOT/Editor/strings.tsv" | sort | uniq -d)
    if [ -n "$dup" ]; then echo "strings.tsv duplicate keys:"; echo "$dup"; FAIL=1; else echo "strings.tsv: no duplicate keys"; fi
fi

[ "$FAIL" -eq 0 ] && echo "verify: OK" || echo "verify: NG"
exit $FAIL
