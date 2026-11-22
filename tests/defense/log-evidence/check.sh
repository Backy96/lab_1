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

interval=unchecked

if [ "$count" -gt 0 ]; then
  verdict=FOUND
else
  verdict=ZERO
fi
echo "read=ok marker=valid interval=$interval matches=$count verdict=$verdict"
