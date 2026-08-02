#!/bin/bash
# VAAPI h264 encoder test script for AMD Vega 64
# Run this on your Debian server to find the working profile

INPUT="/mnt/xmedia/series/Doğu (2021) [imdbid-tt14183052]/Season 4/Doğu (2021) - S04E02 - Burrata [WEBDL-1080p][EAC3 5.1][h265]-TURG.mkv"
OUTPUT="/tmp/vaapi_test.mkv"

echo "=== Testing VAAPI h264 profiles on AMD Vega 64 ==="
echo ""

# Test 1: main profile
echo "TEST 1: main profile"
/usr/lib/jellyfin-ffmpeg/ffmpeg -y -hide_banner -loglevel warning \
  -init_hw_device vaapi=va:/dev/dri/renderD128 -filter_hw_device va \
  -hwaccel vaapi -hwaccel_device va -hwaccel_output_format vaapi \
  -i "$INPUT" -map 0:v:0 -map 0:a -map 0:s? -t 5 \
  -vf "hwdownload,format=nv12,hwupload,scale_vaapi=w=1920:h=-2" \
  -c:v h264_vaapi -profile:v main -qp 24 \
  "$OUTPUT" && echo "SUCCESS: main profile works!" || echo "FAILED: main profile"

# Test 2: baseline profile
echo ""
echo "TEST 2: baseline profile"
/usr/lib/jellyfin-ffmpeg/ffmpeg -y -hide_banner -loglevel warning \
  -init_hw_device vaapi=va:/dev/dri/renderD128 -filter_hw_device va \
  -hwaccel vaapi -hwaccel_device va -hwaccel_output_format vaapi \
  -i "$INPUT" -map 0:v:0 -map 0:a -map 0:s? -t 5 \
  -vf "hwdownload,format=nv12,hwupload,scale_vaapi=w=1920:h=-2" \
  -c:v h264_vaapi -profile:v baseline -qp 24 \
  "$OUTPUT" && echo "SUCCESS: baseline profile works!" || echo "FAILED: baseline profile"

# Test 3: high profile
echo ""
echo "TEST 3: high profile"
/usr/lib/jellyfin-ffmpeg/ffmpeg -y -hide_banner -loglevel warning \
  -init_hw_device vaapi=va:/dev/dri/renderD128 -filter_hw_device va \
  -hwaccel vaapi -hwaccel_device va -hwaccel_output_format vaapi \
  -i "$INPUT" -map 0:v:0 -map 0:a -map 0:s? -t 5 \
  -vf "hwdownload,format=nv12,hwupload,scale_vaapi=w=1920:h=-2" \
  -c:v h264_vaapi -profile:v high -qp 24 \
  "$OUTPUT" && echo "SUCCESS: high profile works!" || echo "FAILED: high profile"

# Test 4: no profile specified (auto)
echo ""
echo "TEST 4: no profile (auto)"
/usr/lib/jellyfin-ffmpeg/ffmpeg -y -hide_banner -loglevel warning \
  -init_hw_device vaapi=va:/dev/dri/renderD128 -filter_hw_device va \
  -hwaccel vaapi -hwaccel_device va -hwaccel_output_format vaapi \
  -i "$INPUT" -map 0:v:0 -map 0:a -map 0:s? -t 5 \
  -vf "hwdownload,format=nv12,hwupload,scale_vaapi=w=1920:h=-2" \
  -c:v h264_vaapi -qp 24 \
  "$OUTPUT" && echo "SUCCESS: auto profile works!" || echo "FAILED: auto profile"

# Test 5: main10 profile (10-bit)
echo ""
echo "TEST 5: main10 profile (10-bit)"
/usr/lib/jellyfin-ffmpeg/ffmpeg -y -hide_banner -loglevel warning \
  -init_hw_device vaapi=va:/dev/dri/renderD128 -filter_hw_device va \
  -hwaccel vaapi -hwaccel_device va -hwaccel_output_format vaapi \
  -i "$INPUT" -map 0:v:0 -map 0:a -map 0:s? -t 5 \
  -vf "hwdownload,format=p010,hwupload,scale_vaapi=w=1920:h=-2" \
  -c:v h264_vaapi -profile:v main10 -qp 24 \
  "$OUTPUT" && echo "SUCCESS: main10 profile works!" || echo "FAILED: main10 profile"

# Cleanup
rm -f "$OUTPUT"

echo ""
echo "=== Check which tests passed ==="
