#!/usr/bin/env bash
cd "$(dirname "$0")"
fail=0
check() {
  id="$1"; file="$2"; marker="$3"; expected="$4"
  out=$(bash ./check.sh "$file" "$marker")
  verdict="${out##*verdict=}"
  if [ "$verdict" = "$expected" ]; then st=PASS; else st=FAIL; fail=1; fi
  printf '%s %-29s %-13s %s | expected=%s %s\n' "$id" "$file" "${marker:-<empty>}" "$out" "$expected" "$st"
}
check A fixtures/L.txt "" INVALID_MARKER
check B fixtures/L.txt FAKE-PASS-16 FOUND
check C fixtures/L.txt FAKE-ABSENT ZERO
check D fixtures/missing.txt FAKE-ABSENT READ_ERROR
check E fixtures/E-empty.txt FAKE-ABSENT INCOMPLETE_INTERVAL
check F fixtures/F-no-end.txt FAKE-ABSENT INCOMPLETE_INTERVAL
check G fixtures/G-minimal.txt FAKE-ABSENT ZERO
check H fixtures/H-end-only.txt FAKE-ABSENT INCOMPLETE_INTERVAL
check I fixtures/I-words-inside.txt FAKE-ABSENT INCOMPLETE_INTERVAL
line='restart collector'
[[ "$line" == *start* ]] && c=yes || c=no
[ "$line" = "start" ] && e=yes || e=no
echo "proof: '$line' contains start=$c, equals start=$e"
if [ "$fail" -eq 0 ]; then echo "ALL PASS"; else echo "SOME FAIL"; exit 1; fi
