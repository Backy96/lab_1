#!/usr/bin/env bash
# Synthetic log evidence checker (lab 6 defense). Only synthetic fixtures, no real logs.
# usage: check.sh <file> <marker>
file="$1"
marker="$2"

if [ -z "$marker" ]; then
  echo "read=skipped marker=invalid interval=unchecked matches=- verdict=INVALID_MARKER"
  exit 0
fi

count=$(grep -c -F -- "$marker" "$file" 2>/dev/null)
rc=$?
# grep: 0 = found, 1 = no match, 2 = read error
if [ "$rc" -eq 2 ]; then
  echo "read=error marker=valid interval=unchecked matches=- verdict=READ_ERROR"
  exit 0
fi

# collector contract: at least 2 lines, first line exactly "start", last line exactly "end"
lines=$(awk 'END { print NR }' "$file")
first=$(head -n 1 "$file")
last=$(tail -n 1 "$file")
if [ "$lines" -ge 2 ] && [ "$first" = "start" ] && [ "$last" = "end" ]; then
  interval=complete
else
  echo "read=ok marker=valid interval=incomplete matches=$count verdict=INCOMPLETE_INTERVAL"
  exit 0
fi

if [ "$count" -gt 0 ]; then
  verdict=FOUND
else
  verdict=ZERO
fi
echo "read=ok marker=valid interval=$interval matches=$count verdict=$verdict"
